namespace Panopticon.Server.Progress;

/// <summary>Done over total tasks. <c>0 of 0</c> is a valid, empty progress, not an error.</summary>
public readonly record struct TaskProgress(int Done, int Total)
{
    public static TaskProgress Empty => new(0, 0);

    public bool IsEmpty => Total == 0;

    public bool IsComplete => Total > 0 && Done == Total;

    public bool HasUnfinished => Done < Total;

    public TaskProgress Add(bool done) => new(Done + (done ? 1 : 0), Total + 1);

    public TaskProgress Add(TaskProgress other) => new(Done + other.Done, Total + other.Total);
}

/// <summary>One level-2 section of a <c>tasks.md</c>.</summary>
public sealed record SectionProgress(int Number, string Title, TaskProgress Progress);

/// <summary>A parsed <c>tasks.md</c>: its sections in document order, and overall progress.</summary>
/// <remarks>Overall includes tasks that come before the first section.</remarks>
public sealed record ParsedTasks(IReadOnlyList<SectionProgress> Sections, TaskProgress Overall);
