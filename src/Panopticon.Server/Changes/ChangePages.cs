using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Changes;

/// <summary>
/// The change page's header (change-documents-and-history D9; spec: change-documents, Document list): the change as its
/// card shows it, whether and when it was archived, and its latest documents in reading order.
/// </summary>
public sealed record ChangePage(
    ChangeCard Card,
    DateTimeOffset? ArchivedAt,
    string? ArchiveFolder,
    IReadOnlyList<DocumentEntry> Documents);

/// <summary>One document in the list. <see cref="Kind"/> is proposal, design, tasks, spec, devlog, meta or other.</summary>
public sealed record DocumentEntry(string Path, string Kind, string Title, int SizeBytes);

/// <summary>A rendered document: HTML made safe by <see cref="DocumentRenderer"/>.</summary>
public sealed record RenderedDocument(string Path, string Html);

/// <summary>One archived change in the history (spec: change-history, History list), with its Project as its card has it.</summary>
/// <param name="ArchiveFolder">Its archive folder; null for a personal copy retired by linking.</param>
/// <param name="LinkedTo">For a retired copy, the Project its work went to (projects D4): "Linked to PAN".</param>
public sealed record HistoryEntry(
    Guid ChangeId,
    CardRepo Repo,
    string Change,
    DateTimeOffset ArchivedAt,
    string? ArchiveFolder,
    IReadOnlyList<CardAgent> Agents,
    CardProject? Project,
    bool Personal,
    CardProject? LinkedTo);

/// <summary>A page of the history, newest archive first; <see cref="NextBefore"/> is the next page's cursor.</summary>
public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Changes, DateTimeOffset? NextBefore);

/// <summary>An owner choice: a host and an owner (which may contain '/').</summary>
public sealed record OwnerChoice(string Host, string Owner);

/// <summary>A person choice: the member's id (what the filter matches) and display name (its label).</summary>
public sealed record MemberChoice(Guid Id, string DisplayName);

/// <summary>
/// The history's filter choices (filters-and-idle-cards D5; spec: change-filters, Filter choices): the distinct values
/// among archived changes, each sorted case-insensitively.
/// </summary>
/// <param name="Projects">The viewer's Projects with an archived change, by code (projects D9).</param>
/// <param name="Personal">Whether the viewer has an archived personal change, so "Personal" is offered.</param>
public sealed record HistoryChoices(
    IReadOnlyList<CardRepo> Repos,
    IReadOnlyList<OwnerChoice> Owners,
    IReadOnlyList<MemberChoice> Members,
    IReadOnlyList<string> Machines,
    IReadOnlyList<string> Clients,
    IReadOnlyList<CardProject> Projects,
    bool Personal);

/// <summary>Reads change pages, documents and the history for the read API.</summary>
public sealed partial class ChangePages(PanopticonDb db, ChangeCards cards, IDocumentStore store, DocumentRenderer renderer)
{
    public const int HistoryPageSize = 50;

    public async Task<ChangePage?> GetAsync(Guid changeId, Viewer viewer, CancellationToken cancellationToken)
    {
        var change = await cards.Query().VisibleTo(viewer).SingleOrDefaultAsync(c => c.Id == changeId, cancellationToken);
        if (change is null)
        {
            return null;
        }

        var documents = await db.ChangeDocuments
            .AsNoTracking()
            .Where(d => d.ChangeId == changeId)
            .Select(d => new { d.Path, d.SizeBytes })
            .ToListAsync(cancellationToken);

        return new ChangePage(
            ChangeCards.ToCard(change),
            change.ArchivedAt,
            change.ArchiveFolder,
            Order(documents.Select(d => Describe(d.Path, d.SizeBytes))));
    }

    /// <summary>The document rendered, or null when the change has no document at that path or the viewer can't see it.</summary>
    /// <exception cref="InvalidOperationException">The document is listed but its content is missing from storage.</exception>
    public async Task<RenderedDocument?> RenderAsync(Guid changeId, string path, Viewer viewer, CancellationToken cancellationToken)
    {
        if (!await db.Changes.AsNoTracking().VisibleTo(viewer).AnyAsync(c => c.Id == changeId, cancellationToken))
        {
            return null;
        }

        var document = await db.ChangeDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.ChangeId == changeId && d.Path == path, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var content = await store.GetAsync(document.Sha256, cancellationToken)
            ?? throw new InvalidOperationException($"Document {document.Sha256} for change {changeId} is missing from storage.");
        return new RenderedDocument(document.Path, renderer.Render(document.Path, document.Sha256, content));
    }

    /// <summary>
    /// Archived changes matching <paramref name="filter"/>, archived strictly before <paramref name="before"/> (all when
    /// null), newest first. The cursor works the same within a filter (filters-and-idle-cards D3).
    /// </summary>
    public async Task<HistoryPage> HistoryAsync(
        HistoryFilter filter,
        DateTimeOffset? before,
        int limit,
        Viewer viewer,
        CancellationToken cancellationToken)
    {
        var query = filter.Apply(cards.Query().Include(c => c.RetiredToProject).VisibleTo(viewer).Where(c => c.ArchivedAt != null));
        if (before is { } given)
        {
            // Npgsql writes only UTC offsets to timestamptz, and a cursor with any other offset is the same instant.
            var cursor = given.ToUniversalTime();
            query = query.Where(c => c.ArchivedAt < cursor);
        }

        // One more than the page, to know whether another page follows. The cursor is the archive time alone (D9), so
        // a page doesn't end inside a run of equal times: it stops before that run, which then starts the next page.
        // Only a page made entirely of one run (50 archives in the same microsecond) can't, and ends at the limit.
        var changes = await query
            .OrderByDescending(c => c.ArchivedAt)
            .ThenByDescending(c => c.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        DateTimeOffset? next = null;
        if (changes.Count > limit)
        {
            var cut = limit;
            var last = changes[limit - 1].ArchivedAt;
            if (changes[limit].ArchivedAt == last)
            {
                var beforeRun = changes.FindLastIndex(limit - 1, c => c.ArchivedAt != last) + 1;
                cut = beforeRun == 0 ? limit : beforeRun;
            }

            changes = changes[..cut];
            next = changes[^1].ArchivedAt;
        }

        return new HistoryPage(
            [.. changes.Select(c =>
            {
                var card = ChangeCards.ToCard(c);
                return new HistoryEntry(c.Id, card.Repo, card.Change, c.ArchivedAt!.Value, c.ArchiveFolder, card.Agents, card.Project, card.Personal, LinkedTo(c));
            })],
            next);
    }

    // projects D4: the Project a retired personal copy's work went to, shown in place of its archive folder.
    private static CardProject? LinkedTo(Change change) =>
        change.RetiredToProject is { } project ? CardProject.Of(project) : null;

    /// <summary>
    /// The filter choices among the archived changes the viewer can see, so a hidden change leaks no value. Values that differ only in case are one choice, shown in one of
    /// the casings reported. An agent with no member adds no person.
    /// </summary>
    public async Task<HistoryChoices> HistoryChoicesAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        var archived = db.Changes.AsNoTracking().VisibleTo(viewer).Where(c => c.ArchivedAt != null);
        var repos = await archived
            .Select(c => new { c.Repo.Host, c.Repo.Owner, c.Repo.Name, c.Repo.HostKey, c.Repo.OwnerKey })
            .Distinct()
            .ToListAsync(cancellationToken);
        var agents = await archived
            .SelectMany(c => c.Agents)
            .Select(a => new
            {
                a.Agent.ClientName,
                a.Agent.ClientNameKey,
                a.Agent.MachineName,
                a.Agent.MachineNameKey,
                a.Agent.MemberId,
                DisplayName = a.Agent.Member == null ? null : a.Agent.Member.DisplayName,
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        // Only Projects the viewer belongs to can appear: VisibleTo hides the rest.
        var projects = await archived
            .Where(c => c.Project != null)
            .Select(c => new { c.Project!.Id, c.Project.Code, c.Project.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        var personal = await archived.AnyAsync(c => c.OwnerMemberId == viewer.MemberId, cancellationToken);

        var byLabel = StringComparer.OrdinalIgnoreCase;
        return new HistoryChoices(
            [.. repos
                .Select(r => new CardRepo(r.Host, r.Owner, r.Name))
                .OrderBy(r => r.Host, byLabel).ThenBy(r => r.Owner, byLabel).ThenBy(r => r.Name, byLabel)],
            [.. repos
                .DistinctBy(r => (r.HostKey, r.OwnerKey))
                .Select(r => new OwnerChoice(r.Host, r.Owner))
                .OrderBy(o => o.Host, byLabel).ThenBy(o => o.Owner, byLabel)],
            [.. agents
                .Where(a => a.MemberId is not null)
                .DistinctBy(a => a.MemberId)
                .Select(a => new MemberChoice(a.MemberId!.Value, a.DisplayName!))
                .OrderBy(m => m.DisplayName, byLabel).ThenBy(m => m.Id)],
            [.. agents.DistinctBy(a => a.MachineNameKey, StringComparer.Ordinal).Select(a => a.MachineName).Order(byLabel)],
            [.. agents.DistinctBy(a => a.ClientNameKey, StringComparer.Ordinal).Select(a => a.ClientName).Order(byLabel)],
            [.. projects.Select(p => new CardProject(p.Id, p.Code, p.Name)).OrderBy(p => p.Code, byLabel)],
            personal);
    }

    /// <summary>Proposal, design, tasks, specs by capability, devlog notes newest first, then anything else by path.</summary>
    internal static IReadOnlyList<DocumentEntry> Order(IEnumerable<DocumentEntry> documents) =>
        [.. documents
            .OrderBy(d => d.Kind switch
            {
                "proposal" => 0,
                "design" => 1,
                "tasks" => 2,
                "spec" => 3,
                "devlog" => 4,
                _ => 5,
            })
            .ThenByDescending(d => d.Kind == "devlog" ? d.Path : string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.Path, StringComparer.Ordinal)];

    internal static DocumentEntry Describe(string path, int sizeBytes)
    {
        var (kind, title) = path switch
        {
            "proposal.md" => ("proposal", "Proposal"),
            "design.md" => ("design", "Design"),
            "tasks.md" => ("tasks", "Tasks"),
            ".openspec.yaml" or ".openspec.yml" => ("meta", path),
            _ when path.StartsWith("specs/", StringComparison.Ordinal) => ("spec", SpecTitle(path)),
            _ when path.StartsWith("devlog/", StringComparison.Ordinal) && !path["devlog/".Length..].Contains('/', StringComparison.Ordinal)
                => ("devlog", DevlogTitle(path["devlog/".Length..])),
            _ => ("other", path),
        };

        return new DocumentEntry(path, kind, title, sizeBytes);
    }

    // specs/<capability>/spec.md → the capability; anything else under specs/ keeps its path below specs/.
    private static string SpecTitle(string path)
    {
        var below = path["specs/".Length..];
        return below.EndsWith("/spec.md", StringComparison.Ordinal) ? below[..^"/spec.md".Length] : below;
    }

    // 20261108-1515-section-1-done.md → "section 1 done · 2026-11-08 15:15"; other names keep their file name.
    private static string DevlogTitle(string fileName)
    {
        var match = DevlogName().Match(fileName);
        if (!match.Success)
        {
            return fileName;
        }

        if (!DateTime.TryParseExact(match.Groups["stamp"].Value, "yyyyMMdd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
        {
            return fileName;
        }

        var slug = match.Groups["slug"].Value.Replace('-', ' ');
        return $"{slug} · {when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex(@"^(?<stamp>\d{8}-\d{4})-(?<slug>.+)\.md$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DevlogName();
}
