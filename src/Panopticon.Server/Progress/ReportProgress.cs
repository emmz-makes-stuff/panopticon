using Panopticon.Server.Data;

namespace Panopticon.Server.Progress;

/// <summary>Fills a report event's progress from its parsed <c>tasks.md</c> (change-metrics D1).</summary>
public static class ReportProgress
{
    /// <summary>
    /// Sets the overall counts, the current section and the numbered sections' counts. A current section numbered only
    /// by its position is left out, since its number could be another section's (spec: Unnumbered section).
    /// </summary>
    public static void Fill(ReportEvent reportEvent, ParsedTasks tasks, SectionProgress? current)
    {
        ArgumentNullException.ThrowIfNull(reportEvent);
        ArgumentNullException.ThrowIfNull(tasks);

        reportEvent.OverallDone = tasks.Overall.Done;
        reportEvent.OverallTotal = tasks.Overall.Total;
        reportEvent.CurrentSection = current is { IsNumbered: true } ? current.Number : null;
        reportEvent.Sections = [.. tasks.Sections
            .Where(s => s.IsNumbered)
            .Select(s => new SectionCounts { Number = s.Number, Title = s.Title, Done = s.Progress.Done, Total = s.Progress.Total })];
    }

    /// <summary>Marks a report whose <c>tasks.md</c> couldn't be read, so the metrics leave it out (D3).</summary>
    public static void MarkUnreadable(ReportEvent reportEvent)
    {
        ArgumentNullException.ThrowIfNull(reportEvent);

        reportEvent.OverallDone = null;
        reportEvent.OverallTotal = ReportEvent.Unreadable;
        reportEvent.CurrentSection = null;
        reportEvent.Sections = null;
    }
}
