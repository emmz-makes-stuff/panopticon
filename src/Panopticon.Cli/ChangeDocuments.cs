namespace Panopticon.Cli;

/// <summary>Reads a change's documents from <c>openspec/changes/&lt;change&gt;/</c> under the repo root.</summary>
internal static class ChangeDocuments
{
    public const string TasksFileName = "tasks.md";

    /// <summary>The current content of the change's <c>tasks.md</c>.</summary>
    public static async Task<string> ReadTasksAsync(string repoRoot, string change, CancellationToken cancellationToken)
    {
        var changeDirectory = ResolveChangeDirectory(repoRoot, change);
        if (!Directory.Exists(changeDirectory))
        {
            throw new BridgeException($"No change '{change}' was found: looked for the folder '{changeDirectory}'.");
        }

        var tasksPath = Path.Combine(changeDirectory, TasksFileName);
        if (!File.Exists(tasksPath))
        {
            throw new BridgeException($"Change '{change}' has no {TasksFileName}: looked for '{tasksPath}'.");
        }

        try
        {
            return await File.ReadAllTextAsync(tasksPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException($"Couldn't read '{tasksPath}': {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The change's folder, refusing any name that could leave <c>openspec/changes/</c>. Only a single plain path
    /// segment is accepted, and the combined path is checked as well.
    /// </summary>
    public static string ResolveChangeDirectory(string repoRoot, string change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var changesRoot = Path.GetFullPath(Path.Combine(repoRoot, "openspec", "changes"));
        if (string.IsNullOrWhiteSpace(change)
            || change is "." or ".."
            || change.Trim() != change
            || change.IndexOfAny(['/', '\\', ':']) >= 0
            || change.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw InvalidName(change);
        }

        var directory = Path.GetFullPath(Path.Combine(changesRoot, change));
        if (Path.GetDirectoryName(directory) != changesRoot)
        {
            throw InvalidName(change);
        }

        return directory;
    }

    private static BridgeException InvalidName(string change) =>
        new($"Invalid change name '{change}': it must be a single folder name under openspec/changes/.");
}
