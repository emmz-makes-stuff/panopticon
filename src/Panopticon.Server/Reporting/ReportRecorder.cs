using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Progress;

namespace Panopticon.Server.Reporting;

/// <summary>Where a change lives: host, owner (may contain '/', e.g. GitLab subgroups) and repo name.</summary>
public sealed record RepoIdentity(string Host, string Owner, string Name);

/// <summary>The reporting agent: its MCP client name and the machine it runs on.</summary>
public sealed record AgentIdentity(string ClientName, string MachineName);

/// <summary>A validated progress report (spec: progress-reporting, Report content).</summary>
public sealed record ReportInput(
    RepoIdentity Repo,
    string ChangeName,
    AgentIdentity Agent,
    int? ReportedSection,
    string TasksMarkdown);

/// <summary>The outcome of recording a report: the change and its newly computed progress.</summary>
public sealed record RecordedReport(
    Guid ChangeId,
    ProgressSnapshot Snapshot,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Records a report: stores <c>tasks.md</c>, upserts repo / change / agent, updates the change's
/// progress snapshot and the agent's last-report time, and appends a report event.
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

        // Content-addressed and idempotent: storing before the database write is safe even if that write fails.
        var tasksSha256 = await documents.PutAsync(report.TasksMarkdown, cancellationToken);
        var receivedAt = clock.GetUtcNow();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var changeId = await UpsertAsync(report, snapshot, tasksSha256, receivedAt, cancellationToken);
                return new RecordedReport(changeId, snapshot, receivedAt);
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsUniqueViolation(ex))
            {
                // A concurrent report created the same repo, change or agent first; retry against it.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<Guid> UpsertAsync(
        ReportInput report,
        ProgressSnapshot snapshot,
        string tasksSha256,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var repo = await FindOrAddRepoAsync(report.Repo, cancellationToken);
        var change = await FindOrAddChangeAsync(repo, report.ChangeName, snapshot, cancellationToken);
        var agent = await FindOrAddAgentAsync(report.Agent, cancellationToken);

        change.Snapshot = snapshot;
        change.LastReportedAt = receivedAt;

        var changeAgent = await db.ChangeAgents.FindAsync([change.Id, agent.Id], cancellationToken);
        if (changeAgent is null)
        {
            db.ChangeAgents.Add(new ChangeAgent { ChangeId = change.Id, AgentId = agent.Id, LastReportedAt = receivedAt });
        }
        else
        {
            changeAgent.LastReportedAt = receivedAt;
        }

        db.ReportEvents.Add(new ReportEvent
        {
            ChangeId = change.Id,
            AgentId = agent.Id,
            ReportedSection = report.ReportedSection,
            TasksSha256 = tasksSha256,
            ReceivedAt = receivedAt,
        });

        await db.SaveChangesAsync(cancellationToken);
        return change.Id;
    }

    private async Task<Repo> FindOrAddRepoAsync(RepoIdentity identity, CancellationToken cancellationToken)
    {
        var (hostKey, ownerKey, nameKey) = (Key(identity.Host), Key(identity.Owner), Key(identity.Name));
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
        var nameKey = Key(name);
        var change = await db.Changes.SingleOrDefaultAsync(
            c => c.RepoId == repo.Id && c.NameKey == nameKey,
            cancellationToken);
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
        var (clientKey, machineKey) = (Key(identity.ClientName), Key(identity.MachineName));
        var agent = await db.Agents.SingleOrDefaultAsync(
            a => a.ClientNameKey == clientKey && a.MachineNameKey == machineKey,
            cancellationToken);
        if (agent is not null)
        {
            return agent;
        }

        agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            ClientName = identity.ClientName,
            MachineName = identity.MachineName,
            ClientNameKey = clientKey,
            MachineNameKey = machineKey,
        };
        db.Agents.Add(agent);
        return agent;
    }

    private static string Key(string value) => value.Trim().ToLowerInvariant();

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
