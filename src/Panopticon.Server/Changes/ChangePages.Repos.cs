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
/// <see cref="ChangePages.RepoArchivedShown"/> archived ones, with <see cref="MoreArchived"/> counting the rest.
/// </summary>
public sealed record RepoSummary(
    CardRepo Repo,
    DateTimeOffset LatestActivityAt,
    IReadOnlyList<RepoActiveChange> Active,
    IReadOnlyList<RepoArchivedChange> Archived,
    int MoreArchived);

/// <summary>The Repos page: every repo with a change the viewer can see, newest activity first (spec: repos, Repos order).</summary>
public sealed record RepoList(IReadOnlyList<RepoSummary> Repos);

public sealed partial class ChangePages
{
    /// <summary>The most archived changes a repo shows on the Repos page (spec: repos, Archived changes on the Repos page).</summary>
    public const int RepoArchivedShown = 5;

    /// <summary>
    /// The repos with a change the viewer can see (repos-page D1). Retired copies are left out, as the metrics leave
    /// them out: their work is on the Project's change.
    /// </summary>
    public async Task<RepoList> ReposAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        var rows = await db.Changes
            .AsNoTracking()
            .VisibleTo(viewer)
            .Where(c => c.RetiredToProjectId == null)
            .Select(c => new
            {
                c.Id,
                c.RepoId,
                c.Repo.Host,
                c.Repo.Owner,
                RepoName = c.Repo.Name,
                c.Name,
                c.LastReportedAt,
                c.ArchivedAt,
                Project = c.Project == null ? null : new CardProject(c.Project.Id, c.Project.Code, c.Project.Name),
                Personal = c.OwnerMemberId != null,
                c.Snapshot.OverallDone,
                c.Snapshot.OverallTotal,
            })
            .ToListAsync(cancellationToken);

        var repos = rows
            .GroupBy(r => r.RepoId)
            .Select(group =>
            {
                var first = group.First();
                var active = group
                    .Where(r => r.ArchivedAt == null)
                    .OrderByDescending(r => r.LastReportedAt)
                    .ThenByDescending(r => r.Id)
                    .Select(r => new RepoActiveChange(r.Id, r.Name, r.Project, r.Personal, r.LastReportedAt, new CardProgress(r.OverallDone, r.OverallTotal)))
                    .ToList();
                var archived = group
                    .Where(r => r.ArchivedAt != null)
                    .OrderByDescending(r => r.ArchivedAt)
                    .ThenByDescending(r => r.Id)
                    .Select(r => new RepoArchivedChange(r.Id, r.Name, r.Project, r.Personal, r.ArchivedAt!.Value))
                    .ToList();
                var latest = group.Max(r => r.ArchivedAt ?? r.LastReportedAt);
                return new RepoSummary(
                    new CardRepo(first.Host, first.Owner, first.RepoName),
                    latest,
                    active,
                    [.. archived.Take(RepoArchivedShown)],
                    Math.Max(0, archived.Count - RepoArchivedShown));
            })
            .OrderByDescending(r => r.LatestActivityAt)
            .ThenBy(r => r.Repo.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Repo.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RepoList(repos);
    }
}
