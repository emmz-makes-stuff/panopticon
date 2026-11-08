using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;

namespace Panopticon.Server.Dashboard;

/// <summary>What one dashboard card shows (spec: live-dashboard, Card content and Two progress bars).</summary>
public sealed record ChangeCard(
    Guid ChangeId,
    CardRepo Repo,
    string Change,
    IReadOnlyList<CardAgent> Agents,
    CardSection? CurrentSection,
    CardProgress Overall,
    DateTimeOffset LastReportedAt);

public sealed record CardRepo(string Host, string Owner, string Name);

/// <summary>An agent on a card. <see cref="Member"/> is its member's display name, or null for a report with no member.</summary>
public sealed record CardAgent(string Client, string Machine, DateTimeOffset LastReportedAt, string? Member = null);

public sealed record CardSection(int Number, string Title, CardProgress Progress);

/// <summary>Done over total; total 0 is the "empty" state, shown as such rather than as 0%.</summary>
public sealed record CardProgress(int Done, int Total);

/// <summary>Builds cards from the database: for the read API, and for live updates after a report.</summary>
public sealed class ChangeCards(PanopticonDb db)
{
    /// <summary>All active changes, most recently reported first; archived ones are in the history instead.</summary>
    public async Task<IReadOnlyList<ChangeCard>> ListAsync(CancellationToken cancellationToken)
    {
        var changes = await Query()
            .Where(c => c.ArchivedAt == null)
            .OrderByDescending(c => c.LastReportedAt)
            .ToListAsync(cancellationToken);
        return changes.ConvertAll(ToCard);
    }

    public async Task<ChangeCard?> GetAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await Query().SingleOrDefaultAsync(c => c.Id == changeId, cancellationToken);
        return change is null ? null : ToCard(change);
    }

    private IQueryable<Change> Query() => db.Changes
        .AsNoTracking()
        .AsSplitQuery()
        .Include(c => c.Repo)
        .Include(c => c.Agents).ThenInclude(a => a.Agent).ThenInclude(a => a.Member);

    private static ChangeCard ToCard(Change change)
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
                .Select(a => new CardAgent(a.Agent.ClientName, a.Agent.MachineName, a.LastReportedAt, a.Agent.Member?.DisplayName))],
            section,
            new CardProgress(snapshot.OverallDone, snapshot.OverallTotal),
            change.LastReportedAt);
    }
}
