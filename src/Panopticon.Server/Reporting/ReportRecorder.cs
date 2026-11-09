using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Progress;

namespace Panopticon.Server.Reporting;

/// <summary>Where a change lives: host, owner (may contain '/', e.g. GitLab subgroups) and repo name.</summary>
public sealed record RepoIdentity(string Host, string Owner, string Name);

/// <summary>
/// The reporting agent: its MCP client name, the machine it runs on, and the member whose token it reported with
/// (design D16). <see cref="MemberId"/> comes from the token, never from the report; it's null for the bootstrap token.
/// </summary>
public sealed record AgentIdentity(string ClientName, string MachineName, Guid? MemberId = null);

/// <summary>A validated progress report (spec: progress-reporting, Report content).</summary>
public sealed record ReportInput(
    RepoIdentity Repo,
    string ChangeName,
    AgentIdentity Agent,
    int? ReportedSection,
    string TasksMarkdown)
{
    /// <summary>
    /// The checked documents (change-documents-and-history D6). Null means just <c>tasks.md</c>, as from an older CLI.
    /// </summary>
    public IReadOnlyList<Contracts.ChangeDocument>? Documents { get; init; }

    /// <summary>The archive folder's name when this is the change's archived report (D7).</summary>
    public string? Archive { get; init; }
}

/// <summary>What a report did to the change's archive state (change-documents-and-history D7).</summary>
public enum ArchiveOutcome
{
    /// <summary>An ordinary report on an active change.</summary>
    None,

    /// <summary>This report archived the change.</summary>
    Archived,

    /// <summary>The change was already archived under that folder: nothing was recorded.</summary>
    AlreadyArchived,
}

/// <summary>The outcome of recording a report: the change and its newly computed progress.</summary>
public sealed record RecordedReport(
    Guid ChangeId,
    ProgressSnapshot Snapshot,
    DateTimeOffset ReceivedAt,
    ArchiveOutcome Archive = ArchiveOutcome.None);

/// <summary>
/// Records a report: stores its documents, upserts repo / change / agent, updates the change's progress snapshot,
/// latest documents and the agent's last-report time, appends a report event with its manifest, and archives the
/// change when the report says so.
/// </summary>
public sealed class ReportRecorder(PanopticonDb db, IDocumentStore documents, TimeProvider clock)
{
    private const int MaxAttempts = 3;

    /// <exception cref="TasksDocumentTooLargeException">The tasks.md is over 1 MiB; nothing is recorded.</exception>
    public async Task<RecordedReport> RecordAsync(ReportInput report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Parse before touching storage, so a rejected document records nothing.
        var parsed = TasksParser.Parse(report.TasksMarkdown);
        var current = CurrentSection.Select(parsed, report.ReportedSection);
        var snapshot = new ProgressSnapshot
        {
            OverallDone = parsed.Overall.Done,
            OverallTotal = parsed.Overall.Total,
            CurrentSectionNumber = current?.Number,
            CurrentSectionTitle = current?.Title,
            CurrentSectionDone = current?.Progress.Done ?? 0,
            CurrentSectionTotal = current?.Progress.Total ?? 0,
            ReportedSection = report.ReportedSection,
        };

        // An older CLI sends no documents, so its manifest holds just the tasks (spec: Report from an older CLI).
        // Otherwise DocumentChecks has made sure the documents include tasks.md, with tasksMarkdown as its content.
        var reported = report.Documents ?? [new Contracts.ChangeDocument(Contracts.DocumentLimits.TasksPath, report.TasksMarkdown)];
        var manifest = reported
            .Select(d => new StoredDocument(d.Path, S3DocumentStore.Hash(d.Content), Encoding.UTF8.GetByteCount(d.Content)))
            .ToList();
        var tasksSha256 = manifest.Single(d => d.Path == Contracts.DocumentLimits.TasksPath).Sha256;
        await StoreNewContentAsync(report, reported, manifest, cancellationToken);

        var receivedAt = clock.GetUtcNow();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // One transaction, so the change row locked in FindOrAddChangeAsync stays locked until the write.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var recorded = await UpsertAsync(report, snapshot, tasksSha256, manifest, receivedAt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return recorded;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsUniqueViolation(ex))
            {
                // A concurrent report created the same repo, change or agent first; retry against it.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<RecordedReport> UpsertAsync(
        ReportInput report,
        ProgressSnapshot snapshot,
        string tasksSha256,
        IReadOnlyList<StoredDocument> manifest,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var repo = await FindOrAddRepoAsync(report.Repo, cancellationToken);

        // D7.1: a change already archived under this folder is final. A second archived report (the watcher and the
        // agent both sending it) changes nothing, not even a new event, and returns the final progress.
        if (report.Archive is { } folder
            && await db.Changes.AsNoTracking().SingleOrDefaultAsync(c => c.RepoId == repo.Id && c.ArchiveFolder == folder, cancellationToken)
                is { } archived)
        {
            return new RecordedReport(archived.Id, archived.Snapshot, receivedAt, ArchiveOutcome.AlreadyArchived);
        }

        var change = await FindOrAddChangeAsync(repo, report.ChangeName, snapshot, cancellationToken);
        var agent = await FindOrAddAgentAsync(report.Agent, cancellationToken);

        // Only a newer report moves the change on: a slower, older report that commits last must not roll the
        // snapshot, the latest documents or the times back. Every report is still kept as an event below.
        if (receivedAt >= change.LastReportedAt)
        {
            change.Snapshot = snapshot;
            change.LastReportedAt = receivedAt;

            // Replaced whole, under the change-row lock taken in FindOrAddChangeAsync (D5).
            await db.ChangeDocuments.Where(d => d.ChangeId == change.Id).ExecuteDeleteAsync(cancellationToken);
            db.ChangeDocuments.AddRange(manifest.Select(d => new LatestDocument
            {
                ChangeId = change.Id,
                Path = d.Path,
                Sha256 = d.Sha256,
                SizeBytes = d.SizeBytes,
                UpdatedAt = receivedAt,
            }));
        }

        // D7.2: the archived report is the change's final state, recorded like any other report first.
        if (report.Archive is not null)
        {
            change.ArchivedAt = receivedAt;
            change.ArchiveFolder = report.Archive;
        }

        var changeAgent = await db.ChangeAgents.FindAsync([change.Id, agent.Id], cancellationToken);
        if (changeAgent is null)
        {
            db.ChangeAgents.Add(new ChangeAgent { ChangeId = change.Id, AgentId = agent.Id, LastReportedAt = receivedAt });
        }
        else if (receivedAt > changeAgent.LastReportedAt)
        {
            changeAgent.LastReportedAt = receivedAt;
        }

        var reportEvent = new ReportEvent
        {
            ChangeId = change.Id,
            AgentId = agent.Id,
            ReportedSection = report.ReportedSection,
            TasksSha256 = tasksSha256,
            ReceivedAt = receivedAt,
        };
        db.ReportEvents.Add(reportEvent);

        // The event's id comes from the database, so its manifest is written after it, in the same transaction.
        await db.SaveChangesAsync(cancellationToken);
        db.ReportDocuments.AddRange(manifest.Select(d => new ReportDocument
        {
            ReportEventId = reportEvent.Id,
            Path = d.Path,
            Sha256 = d.Sha256,
            SizeBytes = d.SizeBytes,
        }));
        await db.SaveChangesAsync(cancellationToken);

        return new RecordedReport(
            change.Id,
            snapshot,
            receivedAt,
            report.Archive is null ? ArchiveOutcome.None : ArchiveOutcome.Archived);
    }

    private sealed record StoredDocument(string Path, string Sha256, int SizeBytes);

    /// <summary>The most store calls a report makes at once.</summary>
    private const int MaxConcurrentStores = 8;

    /// <summary>
    /// Stores the content the change doesn't already hold, a few at a time (review fix 9.2). Content-addressed and
    /// idempotent, so storing before the database write is safe even if that write fails. A hash among the active
    /// change's latest documents is skipped: an earlier report stored it before committing, and objects are never
    /// deleted.
    /// </summary>
    private async Task StoreNewContentAsync(
        ReportInput report,
        IReadOnlyList<Contracts.ChangeDocument> reported,
        IReadOnlyList<StoredDocument> manifest,
        CancellationToken cancellationToken)
    {
        var (hostKey, ownerKey, repoKey) = (IdentityKey.Of(report.Repo.Host), IdentityKey.Of(report.Repo.Owner), IdentityKey.Of(report.Repo.Name));
        var changeKey = IdentityKey.Of(report.ChangeName);
        var held = (await db.ChangeDocuments
                .Where(d => db.Changes.Any(c => c.Id == d.ChangeId
                    && c.ArchivedAt == null
                    && c.NameKey == changeKey
                    && c.Repo.HostKey == hostKey
                    && c.Repo.OwnerKey == ownerKey
                    && c.Repo.NameKey == repoKey))
                .Select(d => d.Sha256)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var toStore = reported
            .Zip(manifest, (document, stored) => (document.Content, stored.Sha256))
            .Where(d => !held.Contains(d.Sha256))
            .DistinctBy(d => d.Sha256, StringComparer.Ordinal)
            .Select(d => d.Content);
        await Parallel.ForEachAsync(
            toStore,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentStores, CancellationToken = cancellationToken },
            async (content, ct) => await documents.PutAsync(content, ct));
    }

    private async Task<Repo> FindOrAddRepoAsync(RepoIdentity identity, CancellationToken cancellationToken)
    {
        var (hostKey, ownerKey, nameKey) = (IdentityKey.Of(identity.Host), IdentityKey.Of(identity.Owner), IdentityKey.Of(identity.Name));
        var repo = await db.Repos.SingleOrDefaultAsync(
            r => r.HostKey == hostKey && r.OwnerKey == ownerKey && r.NameKey == nameKey,
            cancellationToken);
        if (repo is not null)
        {
            return repo;
        }

        repo = new Repo
        {
            Id = Guid.CreateVersion7(),
            Host = identity.Host,
            Owner = identity.Owner,
            Name = identity.Name,
            HostKey = hostKey,
            OwnerKey = ownerKey,
            NameKey = nameKey,
        };
        db.Repos.Add(repo);
        return repo;
    }

    private async Task<Change> FindOrAddChangeAsync(
        Repo repo,
        string name,
        ProgressSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        // FOR UPDATE: concurrent reports on one change take turns, so the only-if-newer check in UpsertAsync
        // compares against the committed state rather than a stale read. Only active changes take part in identity
        // (D5): a name reused after an archive gets a fresh row.
        var nameKey = IdentityKey.Of(name);
        var change = await db.Changes
            .FromSql($"SELECT * FROM changes WHERE repo_id = {repo.Id} AND name_key = {nameKey} AND archived_at IS NULL FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (change is not null)
        {
            return change;
        }

        change = new Change
        {
            Id = Guid.CreateVersion7(),
            RepoId = repo.Id,
            Name = name,
            NameKey = nameKey,
            Snapshot = snapshot,
        };
        db.Changes.Add(change);
        return change;
    }

    private async Task<Agent> FindOrAddAgentAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        var (clientKey, machineKey, memberId) = (IdentityKey.Of(identity.ClientName), IdentityKey.Of(identity.MachineName), identity.MemberId);
        var agent = await db.Agents.SingleOrDefaultAsync(
            a => a.MemberId == memberId && a.ClientNameKey == clientKey && a.MachineNameKey == machineKey,
            cancellationToken);
        if (agent is not null)
        {
            return agent;
        }

        agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            MemberId = memberId,
            ClientName = identity.ClientName,
            MachineName = identity.MachineName,
            ClientNameKey = clientKey,
            MachineNameKey = machineKey,
        };
        db.Agents.Add(agent);
        return agent;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
