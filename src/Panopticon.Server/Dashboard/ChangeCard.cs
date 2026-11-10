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
    bool Personal = false)
{
    /// <summary>Its Project's tags put on it, by key then value (tags D6). The same for every viewer of the change.</summary>
    public IReadOnlyList<ProjectTagRef> ProjectTags { get; init; } = [];

    /// <summary>
    /// The viewer's own personal tags on it, from their agents only (tags D6), by key then value. Empty on the shared
    /// card a live update publishes: the stream fills it in per subscriber.
    /// </summary>
    public IReadOnlyList<PersonalTagRef> PersonalTags { get; init; } = [];
}

/// <summary>
/// A card for everyone who can see it (tags D6): <see cref="Card"/> carries no personal tags, and
/// <see cref="PersonalTags"/> holds each member's, by member id. The map stays on the server: only
/// <see cref="For"/>'s result is ever written to anyone.
/// </summary>
public sealed record LiveCard(ChangeCard Card, IReadOnlyDictionary<Guid, IReadOnlyList<PersonalTagRef>> PersonalTags)
{
    /// <summary>The card as <paramref name="memberId"/> sees it: with their own personal tags, and nobody else's.</summary>
    public ChangeCard For(Guid memberId) =>
        PersonalTags.TryGetValue(memberId, out var tags) ? Card with { PersonalTags = tags } : Card;
}

/// <summary>A personal tag as a card shows it: its key and value, with no id (tags D6).</summary>
public sealed record PersonalTagRef(string Key, string? Value);

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
        return changes.ConvertAll(c => ToCard(c, viewer.MemberId));
    }

    /// <summary>
    /// The change's card for a live update, or null when it's gone or archived: an archived change has no card to
    /// update. It isn't scoped to a viewer: the live stream filters each event by its subscriber (projects D3), and
    /// gives each their own personal tags from the map (tags D6).
    /// </summary>
    public async Task<LiveCard?> GetAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await Query().SingleOrDefaultAsync(c => c.Id == changeId && c.ArchivedAt == null, cancellationToken);
        return change is null ? null : ToLive(change);
    }

    /// <summary>A change's card, active or archived, and whether it's archived (for tagging, which works on both).</summary>
    public async Task<(LiveCard Card, bool Archived)> GetIncludingArchivedAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await Query().SingleAsync(c => c.Id == changeId, cancellationToken);
        return (ToLive(change), change.ArchivedAt is not null);
    }

    internal IQueryable<Change> Query() => db.Changes
        .AsNoTracking()
        .AsSplitQuery()
        .Include(c => c.Repo)
        .Include(c => c.Project)
        .Include(c => c.ProjectTags).ThenInclude(t => t.Tag)
        .Include(c => c.Agents).ThenInclude(a => a.Agent).ThenInclude(a => a.Member)
        .Include(c => c.Agents).ThenInclude(a => a.Tags);

    /// <summary>
    /// The change's card, with <paramref name="viewerMemberId"/>'s personal tags on it; with none when null, as the
    /// shared card for the live stream is (tags D6). Only the viewer's own agents' tags are read, so no other member's
    /// can reach the card.
    /// </summary>
    internal static ChangeCard ToCard(Change change, Guid? viewerMemberId = null)
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
            change.OwnerMemberId is not null)
        {
            ProjectTags = [.. change.ProjectTags
                .Select(t => t.Tag)
                .InTagOrder(t => t.KeyKey, t => t.ValueKey)
                .Select(t => new ProjectTagRef(t.Id, t.Key, t.Value))],
            PersonalTags = viewerMemberId is { } memberId ? PersonalTagsOf(change, memberId) : [],
        };
    }

    private static LiveCard ToLive(Change change) =>
        new(
            ToCard(change),
            change.Agents
                .Where(a => a.Agent.MemberId is not null && a.Tags.Count > 0)
                .Select(a => a.Agent.MemberId!.Value)
                .Distinct()
                .ToDictionary(m => m, m => PersonalTagsOf(change, m)));

    /// <summary>
    /// One member's personal tags on a change: the union over their agents, one per tag regardless of case (in the
    /// casing of the agent that reported most recently), by key then value (tags D5, D6).
    /// </summary>
    internal static IReadOnlyList<PersonalTagRef> PersonalTagsOf(Change change, Guid memberId) =>
        [.. change.Agents
            .Where(a => a.Agent.MemberId == memberId)
            .OrderByDescending(a => a.LastReportedAt)
            .SelectMany(a => a.Tags)
            .DistinctBy(t => (t.KeyKey, t.ValueKey))
            .InTagOrder(t => t.KeyKey, t => t.ValueKey)
            .Select(t => new PersonalTagRef(t.Key, t.Value))];
}
