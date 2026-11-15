using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Changes;

/// <summary>
/// An active change on the Repos page (repos-page D1, D6): its name, its Project or that it's personal, its last report,
/// and its whole-change progress, as its card has it.
/// </summary>
public sealed record RepoActiveChange(
    Guid ChangeId, string Change, CardProject? Project, bool Personal, DateTimeOffset LastReportedAt, CardProgress Overall);

/// <summary>An archived change on the Repos page (repos-page D1).</summary>
public sealed record RepoArchivedChange(Guid ChangeId, string Change, CardProject? Project, bool Personal, DateTimeOffset ArchivedAt);

/// <summary>
/// One repo on the Repos page (spec: repos, Repos list): its active changes, newest report first, then its newest
/// <see cref="ChangePages.RepoArchivedShown"/> archived ones. <see cref="HistoryCount"/> counts every archived change
/// History lists for the repo, retired copies included, so "All N in History" matches that page.
/// </summary>
public sealed record RepoSummary(
    CardRepo Repo,
    IReadOnlyList<RepoActiveChange> Active,
    IReadOnlyList<RepoArchivedChange> Archived,
    int HistoryCount);

/// <summary>The Repos page: every repo with a change the viewer can see, newest activity first (spec: repos, Repos order).</summary>
public sealed record RepoList(IReadOnlyList<RepoSummary> Repos);

public sealed partial class ChangePages
{
    /// <summary>The most archived changes a repo shows on the Repos page (spec: repos, Archived changes on the Repos page).</summary>
    public const int RepoArchivedShown = 5;

    /// <summary>
    /// The repos with a change the viewer can see (repos-page D1), in three bounded queries: the active changes, the
    /// newest archived ones per repo, and a count per repo of what History lists. Retired copies are left out of the
    /// first two, as the metrics leave them out (their work is on the Project's change), but History lists them, so
    /// the count has them.
    /// </summary>
    public async Task<RepoList> ReposAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        var visible = db.Changes.AsNoTracking().VisibleTo(viewer);

        var active = await visible
            .Where(c => c.ArchivedAt == null && c.RetiredToProjectId == null)
            .Select(c => new
            {
                c.RepoId,
                Repo = new CardRepo(c.Repo.Host, c.Repo.Owner, c.Repo.Name),
                Change = new RepoActiveChange(
                    c.Id,
                    c.Name,
                    c.Project == null ? null : new CardProject(c.Project.Id, c.Project.Code, c.Project.Name),
                    c.OwnerMemberId != null,
                    c.LastReportedAt,
                    new CardProgress(c.Snapshot.OverallDone, c.Snapshot.OverallTotal)),
            })
            .ToListAsync(cancellationToken);

        // Top N per repo: EF turns the correlated Take into ROW_NUMBER() OVER (PARTITION BY repo_id ORDER BY archived_at
        // DESC, id DESC) <= N, so only the shown rows leave the database.
        var shownArchive = visible.Where(c => c.ArchivedAt != null && c.RetiredToProjectId == null);
        var archived = await shownArchive
            .Select(c => c.RepoId)
            .Distinct()
            .SelectMany(repoId => shownArchive
                .Where(c => c.RepoId == repoId)
                .OrderByDescending(c => c.ArchivedAt)
                .ThenByDescending(c => c.Id)
                .Take(RepoArchivedShown))
            .Select(c => new
            {
                c.RepoId,
                Repo = new CardRepo(c.Repo.Host, c.Repo.Owner, c.Repo.Name),
                Change = new RepoArchivedChange(
                    c.Id,
                    c.Name,
                    c.Project == null ? null : new CardProject(c.Project.Id, c.Project.Code, c.Project.Name),
                    c.OwnerMemberId != null,
                    c.ArchivedAt!.Value),
            })
            .ToListAsync(cancellationToken);

        // What History lists for the repo: every visible archived change, retired copies included.
        var historyCounts = await visible
            .Where(c => c.ArchivedAt != null)
            .GroupBy(c => c.RepoId)
            .Select(g => new { RepoId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.RepoId, r => r.Count, cancellationToken);

        var activeByRepo = active.ToLookup(r => r.RepoId);
        var archivedByRepo = archived.ToLookup(r => r.RepoId);
        var repos = activeByRepo.Select(g => g.Key).Union(archivedByRepo.Select(g => g.Key))
            .Select(repoId =>
            {
                var activeChanges = activeByRepo[repoId].Select(r => r.Change)
                    .OrderByDescending(c => c.LastReportedAt)
                    .ThenByDescending(c => c.ChangeId)
                    .ToList();
                var archivedChanges = archivedByRepo[repoId].Select(r => r.Change)
                    .OrderByDescending(c => c.ArchivedAt)
                    .ThenByDescending(c => c.ChangeId)
                    .ToList();

                // The newest archive is among the shown ones, so this is the repo's latest report or archive.
                var latest = activeChanges.Select(c => c.LastReportedAt).Concat(archivedChanges.Select(c => c.ArchivedAt)).Max();
                var repo = activeByRepo[repoId].Select(r => r.Repo).FirstOrDefault() ?? archivedByRepo[repoId].First().Repo;
                return (Latest: latest, Summary: new RepoSummary(repo, activeChanges, archivedChanges, historyCounts.GetValueOrDefault(repoId)));
            })
            .OrderByDescending(r => r.Latest)
            .ThenBy(r => r.Summary.Repo.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Summary.Repo.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Summary.Repo.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Summary)
            .ToList();

        return new RepoList(repos);
    }
}
