using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Metrics;

/// <summary>A change's metrics (change-metrics D4; spec: Change page metrics).</summary>
/// <param name="DurationSeconds">Its time to archive, or its time so far while <see cref="Active"/>.</param>
/// <param name="Sections">Its measured sections, in number order.</param>
public sealed record ChangeMetrics(
    DateTimeOffset FirstReportedAt,
    DateTimeOffset? ArchivedAt,
    long DurationSeconds,
    bool Active,
    IReadOnlyList<SectionMetric> Sections);

/// <param name="DurationSeconds">Its time, or its time so far while <see cref="InProgress"/>.</param>
public sealed record SectionMetric(
    int Number,
    string Title,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long DurationSeconds,
    bool InProgress);

/// <summary>The team's metrics over archived changes (D4; spec: Metrics page).</summary>
/// <param name="Changes">How many archived changes match.</param>
/// <param name="MedianArchiveSeconds">Their median time to archive; null when none match.</param>
/// <param name="MedianSectionSeconds">The median time of their finished sections; null when they have none.</param>
/// <param name="Weeks">The last 12 Monday-start UTC weeks by archive date, oldest first.</param>
/// <param name="SlowestSections">Their 10 slowest finished sections, slowest first.</param>
public sealed record TeamMetrics(
    int Changes,
    long? MedianArchiveSeconds,
    long? MedianSectionSeconds,
    IReadOnlyList<WeekMetric> Weeks,
    IReadOnlyList<SlowSection> SlowestSections);

/// <param name="MedianArchiveSeconds">The median time to archive of the changes archived that week; null when none were.</param>
public sealed record WeekMetric(DateTimeOffset WeekStart, int Changes, long? MedianArchiveSeconds);

public sealed record SlowSection(Guid ChangeId, string Change, CardRepo Repo, int Number, string Title, long DurationSeconds);

/// <summary>Reads metrics from the stored per-report progress, with no document reads (D2, D4).</summary>
public sealed class MetricsReader(PanopticonDb db, TimeProvider clock)
{
    public const int WeekCount = 12;
    public const int SlowestCount = 10;

    /// <summary>The change's metrics, or null when there's no such change or the viewer can't see it.</summary>
    public async Task<ChangeMetrics?> ForChangeAsync(Guid changeId, Viewer viewer, CancellationToken cancellationToken)
    {
        var change = await db.Changes.AsNoTracking()
            .VisibleTo(viewer)
            .Where(c => c.Id == changeId)
            .Select(c => new { c.ArchivedAt, c.LastReportedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (change is null)
        {
            return null;
        }

        var events = await db.ReportEvents.AsNoTracking().Where(e => e.ChangeId == changeId).ToListAsync(cancellationToken);

        // An archived change's clock stops at its archive, for a section left unfinished as for the whole.
        var now = change.ArchivedAt ?? clock.GetUtcNow();

        // A change always has its first report as an event; LastReportedAt stands in only if that's ever not so.
        var first = events.Count > 0 ? events.Min(e => e.ReceivedAt) : change.LastReportedAt;
        return new ChangeMetrics(
            first,
            change.ArchivedAt,
            Seconds(SectionTimes.ToArchive(first, change.ArchivedAt, now)),
            change.ArchivedAt is null,
            [.. SectionTimes.Compute(events).Select(s => new SectionMetric(
                s.Number, s.Title, s.StartedAt, s.FinishedAt, Seconds(s.Duration(now)), s.InProgress))]);
    }

    /// <summary>The metrics over the archived changes the viewer can see that the filter matches.</summary>
    public async Task<TeamMetrics> ForTeamAsync(HistoryFilter filter, Viewer viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var archived = filter.Apply(db.Changes.AsNoTracking().VisibleTo(viewer).Where(c => c.ArchivedAt != null));
        var changes = await archived
            .Select(c => new
            {
                c.Id,
                c.Name,
                ArchivedAt = c.ArchivedAt!.Value,
                Repo = new CardRepo(c.Repo.Host, c.Repo.Owner, c.Repo.Name),
            })
            .ToListAsync(cancellationToken);
        var eventsByChange = (await db.ReportEvents.AsNoTracking()
                .Where(e => archived.Select(c => c.Id).Contains(e.ChangeId))
                .ToListAsync(cancellationToken))
            .ToLookup(e => e.ChangeId);

        var measured = changes
            .Where(c => eventsByChange[c.Id].Any())
            .Select(c =>
            {
                var events = eventsByChange[c.Id].ToList();
                var archiveSeconds = Seconds(SectionTimes.ToArchive(events.Min(e => e.ReceivedAt), c.ArchivedAt, c.ArchivedAt));
                var sections = SectionTimes.Compute(events).Where(s => !s.InProgress);
                return new
                {
                    c.ArchivedAt,
                    ArchiveSeconds = archiveSeconds,
                    Sections = sections.Select(s => new SlowSection(c.Id, c.Name, c.Repo, s.Number, s.Title, Seconds(s.Duration(c.ArchivedAt)))).ToList(),
                };
            })
            .ToList();

        var thisWeek = WeekStart(clock.GetUtcNow());
        var weeks = Enumerable.Range(0, WeekCount)
            .Select(i => thisWeek.AddDays(-7 * (WeekCount - 1 - i)))
            .Select(start =>
            {
                var inWeek = measured.Where(m => WeekStart(m.ArchivedAt) == start).Select(m => m.ArchiveSeconds).ToList();
                return new WeekMetric(start, inWeek.Count, Median(inWeek));
            })
            .ToList();

        var sections = measured.SelectMany(m => m.Sections).ToList();
        return new TeamMetrics(
            measured.Count,
            Median([.. measured.Select(m => m.ArchiveSeconds)]),
            Median([.. sections.Select(s => s.DurationSeconds)]),
            weeks,
            [.. sections
                .OrderByDescending(s => s.DurationSeconds)
                .ThenBy(s => s.Change, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Number)
                .Take(SlowestCount)]);
    }

    /// <summary>The Monday 00:00 UTC that starts the week holding <paramref name="at"/>.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        var daysSinceMonday = ((int)utc.DayOfWeek + 6) % 7;
        return new DateTimeOffset(utc.Date.AddDays(-daysSinceMonday), TimeSpan.Zero);
    }

    /// <summary>The middle value, or the mean of the two middle values rounded down; null for no values.</summary>
    public static long? Median(IReadOnlyList<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static long Seconds(TimeSpan duration) => (long)duration.TotalSeconds;
}
