namespace Panopticon.Contracts;

/// <summary>
/// What a report may carry of a change's folder (change-documents-and-history D2 and D6). The bridge checks these
/// before sending, and the server checks them again, since it is the trust boundary. Sizes are UTF-8 byte counts.
/// </summary>
public static class DocumentLimits
{
    public const string TasksPath = "tasks.md";

    /// <summary>The existing <c>tasks.md</c> limit (task-progress): 1 MiB.</summary>
    public const int MaxTasksBytes = 1024 * 1024;

    /// <summary>Any other document: 256 KB (binary, like the tasks limit).</summary>
    public const int MaxDocumentBytes = 256 * 1024;

    /// <summary>All of a report's documents together: 4 MB (binary).</summary>
    public const int MaxTotalBytes = 4 * 1024 * 1024;

    /// <summary>The longest relative path a document may have.</summary>
    public const int MaxPathLength = 512;

    /// <summary>The extensions synced: Markdown and YAML, compared without regard to case.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".md", ".yaml", ".yml"];

    /// <summary>
    /// Why <paramref name="path"/> isn't a plain relative path inside a change's folder ('/' separators, no rooted or
    /// drive paths, no empty, '.' or '..' segments, no '\' or control characters, at most
    /// <see cref="MaxPathLength"/> characters), or null when it is. The bridge and the server apply the same rules.
    /// </summary>
    public static string? PathProblem(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0)
        {
            return "is empty";
        }

        if (path.Length > MaxPathLength)
        {
            return $"is longer than {MaxPathLength} characters";
        }

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            return "contains '\\'";
        }

        if (path[0] == '/' || Path.IsPathRooted(path) || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
        {
            return "is rooted";
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0)
            {
                return "has an empty segment";
            }

            if (segment is "." or "..")
            {
                return $"has a '{segment}' segment";
            }

            if (segment.Any(char.IsControl))
            {
                return "contains a control character";
            }
        }

        return null;
    }

    public static bool HasSyncedExtension(string path) =>
        Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>The limit for the document at <paramref name="path"/>.</summary>
    public static int MaxBytesFor(string path) =>
        string.Equals(path, TasksPath, StringComparison.Ordinal) ? MaxTasksBytes : MaxDocumentBytes;

    /// <summary>The limit as the spec words it, for error messages.</summary>
    public static string Describe(int limit) => limit switch
    {
        MaxTasksBytes => "1 MiB",
        MaxDocumentBytes => "256 KB",
        MaxTotalBytes => "4 MB",
        _ => $"{limit} bytes",
    };
}
