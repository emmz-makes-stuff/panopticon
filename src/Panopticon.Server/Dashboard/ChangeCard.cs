using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Dashboard;

/// <summary>What one dashboard card shows (spec: live-dashboard, Card content and Two progress bars).</summary>
/// <param name="Project">The change's Project, or null for personal and bootstrap work (projects D9).</param>
/// <param name="Personal">Whether it's its owner's personal work (shown as "Personal"); false for a bootstrap change.</param>
public sealed record ChangeCard(
    Guid ChangeId,
    CardRepo Repo,
    string Change,
    IReadOnlyList<CardAgent> Agents,
    CardSection? CurrentSection,
    CardProgress Overall,
    DateTimeOffset LastReportedAt,
    CardProject? Project = null,
    bool Personal = false);

/// <summary>A change's Project on its card, history row and page: chosen by id, shown by its code (projects D9).</summary>
public sealed record CardProject(Guid Id, string Code, string Name)
{
    public static CardProject Of(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new(project.Id, project.Code, project.Name);
    }
}

public sealed record CardRepo(string Host, string Owner, string Name);

/// <summary>
/// An agent on a card. <see cref="Member"/> is its member's display name and <see cref="MemberId"/> their id (the person
/// filter matches on it, filters-and-idle-cards D6); both are null for a report with no member.
/// </summary>
public sealed record CardAgent(string Client, string Machine, DateTimeOffset LastReportedAt, string? Member = null, Guid? MemberId = null);

public sealed record CardSection(int Number, string Title, CardProgress Progress);

/// <summary>Done over total; total 0 is the "empty" state, shown as such rather than as 0%.</summary>
public sealed record CardProgress(int Done, int Total);

/// <summary>Builds cards from the database: for the read API, and for live updates after a report.</summary>
public sealed class ChangeCards(PanopticonDb db)
{
    /// <summary>The active changes the viewer can see, most recently reported first; archived ones are in the history.</summary>
    public async Task<IReadOnlyList<ChangeCard>> ListAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        var changes = await Query()
            .VisibleTo(viewer)
            .Where(c => c.ArchivedAt == null)
            .OrderByDescending(c => c.LastReportedAt)
            .ToListAsync(cancellationToken);
        return changes.ConvertAll(ToCard);
    }

    /// <summary>
    /// The change's card for a live update, or null when it's gone or archived: an archived change has no card to
    /// update. It isn't scoped to a viewer: the live stream filters each event by its subscriber (projects D3).
    /// </summary>
    public async Task<ChangeCard?> GetAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await Query().SingleOrDefaultAsync(c => c.Id == changeId && c.ArchivedAt == null, cancellationToken);
        return change is null ? null : ToCard(change);
    }

    internal IQueryable<Change> Query() => db.Changes
        .AsNoTracking()
        .AsSplitQuery()
        .Include(c => c.Repo)
        .Include(c => c.Project)
        .Include(c => c.Agents).ThenInclude(a => a.Agent).ThenInclude(a => a.Member);

    internal static ChangeCard ToCard(Change change)
    {
        var snapshot = change.Snapshot;
        var section = snapshot.CurrentSectionNumber is { } number
            ? new CardSection(
                number,
                snapshot.CurrentSectionTitle ?? string.Empty,
                new CardProgress(snapshot.CurrentSectionDone, snapshot.CurrentSectionTotal))
            : null;

        return new ChangeCard(
            change.Id,
            new CardRepo(change.Repo.Host, change.Repo.Owner, change.Repo.Name),
            change.Name,
            [.. change.Agents
                .OrderByDescending(a => a.LastReportedAt)
                .Select(a => new CardAgent(a.Agent.ClientName, a.Agent.MachineName, a.LastReportedAt, a.Agent.Member?.DisplayName, a.Agent.MemberId))],
            section,
            new CardProgress(snapshot.OverallDone, snapshot.OverallTotal),
            change.LastReportedAt,
            change.Project is { } project ? CardProject.Of(project) : null,
            change.OwnerMemberId is not null);
    }
}
