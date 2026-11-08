namespace Panopticon.Server.Progress;

/// <summary>Chooses the section a change is "on" (spec: task-progress, Current section).</summary>
public static class CurrentSection
{
    /// <summary>
    /// The reported section if it exists; otherwise the first section with an unfinished task;
    /// otherwise, when everything is done, the last section that has tasks. Null when no section has tasks.
    /// </summary>
    public static SectionProgress? Select(ParsedTasks tasks, int? reportedSection)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (reportedSection is { } reported
            && tasks.Sections.FirstOrDefault(s => s.Number == reported) is { } match)
        {
            return match;
        }

        return tasks.Sections.FirstOrDefault(s => s.Progress.HasUnfinished)
            ?? tasks.Sections.LastOrDefault(s => !s.Progress.IsEmpty);
    }
}
