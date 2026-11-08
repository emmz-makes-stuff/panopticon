using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;

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

/// <summary>One archived change in the history (spec: change-history, History list).</summary>
public sealed record HistoryEntry(
    Guid ChangeId,
    CardRepo Repo,
    string Change,
    DateTimeOffset ArchivedAt,
    string ArchiveFolder,
    IReadOnlyList<CardAgent> Agents);

/// <summary>A page of the history, newest archive first; <see cref="NextBefore"/> is the next page's cursor.</summary>
public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Changes, DateTimeOffset? NextBefore);

/// <summary>Reads change pages, documents and the history for the read API.</summary>
public sealed partial class ChangePages(PanopticonDb db, ChangeCards cards, IDocumentStore store, DocumentRenderer renderer)
{
    public const int HistoryPageSize = 50;

    public async Task<ChangePage?> GetAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await cards.Query().SingleOrDefaultAsync(c => c.Id == changeId, cancellationToken);
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

    /// <summary>The document rendered, or null when the change has no document at that path.</summary>
    /// <exception cref="InvalidOperationException">The document is listed but its content is missing from storage.</exception>
    public async Task<RenderedDocument?> RenderAsync(Guid changeId, string path, CancellationToken cancellationToken)
    {
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

    /// <summary>Archived changes archived strictly before <paramref name="before"/> (all when null), newest first.</summary>
    public async Task<HistoryPage> HistoryAsync(DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        var query = cards.Query().Where(c => c.ArchivedAt != null);
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
                return new HistoryEntry(c.Id, card.Repo, card.Change, c.ArchivedAt!.Value, c.ArchiveFolder!, card.Agents);
            })],
            next);
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
