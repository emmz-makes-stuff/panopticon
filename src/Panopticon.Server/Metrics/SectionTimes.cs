using Panopticon.Server.Data;

namespace Panopticon.Server.Metrics;

/// <summary>A measured section: when work on it started and, once every task stays ticked, when it finished.</summary>
public sealed record SectionTime(int Number, string Title, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt)
{
    public bool InProgress => FinishedAt is null;

    /// <summary>Its time, or its time so far while in progress.</summary>
    public TimeSpan Duration(DateTimeOffset now) => (FinishedAt ?? now) - StartedAt;
}

/// <summary>Time per section and time to archive (change-metrics D2; spec: Time per section, Time to archive).</summary>
public static class SectionTimes
{
    /// <summary>
    /// Walks a change's reports in the order received and measures each numbered section of its latest readable
    /// report that has tasks there.
    /// <list type="bullet">
    /// <item><b>Start:</b> the first report where the section has a ticked task or is the current section.</item>
    /// <item><b>Finish:</b> the first report of the final run in which every one of its tasks is ticked, through the
    /// last report; a report without the section breaks a run.</item>
    /// </list>
    /// A section never started has no time. Reports whose progress isn't known (backfill pending, or unreadable) are
    /// skipped. Sections come back in their number's order; a number is matched across reports, with its latest title.
    /// </summary>
    public static IReadOnlyList<SectionTime> Compute(IEnumerable<ReportEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var readable = events
            .Where(IsReadable)
            .OrderBy(e => e.ReceivedAt)
            .ThenBy(e => e.Id)
            .ToList();
        if (readable.Count == 0)
        {
            return [];
        }

        var latest = readable[^1].Sections!
            .Where(s => s.Total > 0)
            .GroupBy(s => s.Number)
            .Select(g => g.First());

        var times = new List<SectionTime>();
        foreach (var section in latest.OrderBy(s => s.Number))
        {
            DateTimeOffset? startedAt = null;
            DateTimeOffset? runStart = null;
            foreach (var reportEvent in readable)
            {
                var counts = reportEvent.Sections!.FirstOrDefault(s => s.Number == section.Number);
                if (startedAt is null && ((counts?.Done ?? 0) > 0 || reportEvent.CurrentSection == section.Number))
                {
                    startedAt = reportEvent.ReceivedAt;
                }

                var complete = counts is { Total: > 0 } && counts.Done == counts.Total;
                runStart = complete ? runStart ?? reportEvent.ReceivedAt : null;
            }

            if (startedAt is { } started)
            {
                times.Add(new SectionTime(section.Number, section.Title, started, runStart));
            }
        }

        return times;
    }

    /// <summary>
    /// From the first report to the archive; while active, to <paramref name="now"/> (spec: Time to archive).
    /// </summary>
    public static TimeSpan ToArchive(DateTimeOffset firstReportedAt, DateTimeOffset? archivedAt, DateTimeOffset now) =>
        (archivedAt ?? now) - firstReportedAt;

    /// <summary>Whether a report's progress is known: filled in, and its <c>tasks.md</c> was readable.</summary>
    public static bool IsReadable(ReportEvent reportEvent)
    {
        ArgumentNullException.ThrowIfNull(reportEvent);
        return reportEvent.OverallTotal is >= 0 && reportEvent.Sections is not null;
    }
}
