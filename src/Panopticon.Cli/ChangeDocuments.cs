using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Panopticon.Contracts;

namespace Panopticon.Cli;

/// <summary>
/// A change's folder as read for a report: its Markdown and YAML files, its <c>tasks.md</c>, and the archive folder's
/// name when the change has been archived.
/// </summary>
internal sealed record ChangeFolder(string? Archive, string Tasks, IReadOnlyList<ChangeDocument> Documents);

/// <summary>
/// Reads a change's documents from <c>openspec/changes/&lt;change&gt;/</c> under the repo root, or from its latest
/// <c>openspec/changes/archive/&lt;YYYY-MM-DD&gt;-&lt;change&gt;/</c> once it has been archived
/// (change-documents-and-history D2 and D3; spec: cli-bridge).
/// </summary>
internal static partial class ChangeDocuments
{
    public const string TasksFileName = DocumentLimits.TasksPath;
    public const string ArchiveFolderName = "archive";

    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The change's folder, read now. Nothing is cached: each report reads the files as they are.</summary>
    /// <exception cref="BridgeException">An invalid name, no folder, no <c>tasks.md</c>, an unreadable file, or a limit.</exception>
    public static async Task<ChangeFolder> ReadAsync(string repoRoot, string change, CancellationToken cancellationToken)
    {
        var changeDirectory = ResolveChangeDirectory(repoRoot, change);
        string? archive = null;
        var directory = changeDirectory;
        if (!Directory.Exists(changeDirectory))
        {
            directory = FindArchive(repoRoot, change)
                ?? throw new BridgeException(
                    $"No change '{change}' was found: looked for the folder '{changeDirectory}', and for an archived " +
                    $"'{Path.Combine(ChangesRoot(repoRoot), ArchiveFolderName, $"<YYYY-MM-DD>-{change}")}'.");
            archive = Path.GetFileName(directory);
        }

        var tasksPath = Path.Combine(directory, TasksFileName);
        if (!File.Exists(tasksPath) || IsLink(new FileInfo(tasksPath)))
        {
            throw new BridgeException($"Change '{change}' has no {TasksFileName}: looked for '{tasksPath}'.");
        }

        var files = new List<(string Relative, FileInfo File)>();
        Collect(new DirectoryInfo(directory), string.Empty, files);
        files.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        if (files.Count > DocumentLimits.MaxDocuments)
        {
            throw new BridgeException(string.Create(
                CultureInfo.InvariantCulture,
                $"The change has {files.Count} Markdown and YAML files; the limit is {DocumentLimits.MaxDocuments} documents. Nothing was sent."));
        }

        // Sizes are checked before anything is read. Strict UTF-8 decoding gives back the same bytes less any BOM, so
        // the decoded text the server counts can't be larger. (A file grown since is caught by the server's checks.)
        long total = 0;
        foreach (var (relative, file) in files)
        {
            CheckSize(relative, file.Length);
            total += file.Length;
        }

        CheckTotal(total);

        var documents = new List<ChangeDocument>(files.Count);
        foreach (var (relative, file) in files)
        {
            documents.Add(new ChangeDocument(relative, await ReadUtf8Async(file.FullName, relative, cancellationToken)));
        }

        // On a case-insensitive disk, File.Exists above also finds 'Tasks.md', but the server needs 'tasks.md' exactly.
        var tasks = documents.SingleOrDefault(d => d.Path == TasksFileName)?.Content
            ?? throw new BridgeException(
                $"Change '{change}' has no {TasksFileName}: looked for '{tasksPath}', and the file there is named differently. Rename it to '{TasksFileName}'.");
        return new ChangeFolder(archive, tasks, documents);
    }

    /// <summary>
    /// The latest archive of the change: the greatest (ordinal) name matching <c>&lt;YYYY-MM-DD&gt;-&lt;change&gt;</c>
    /// under <c>openspec/changes/archive/</c>, which is the latest date. Null when there is none.
    /// </summary>
    public static string? FindArchive(string repoRoot, string change)
    {
        var archiveRoot = new DirectoryInfo(Path.Combine(ChangesRoot(repoRoot), ArchiveFolderName));
        if (!archiveRoot.Exists)
        {
            return null;
        }

        try
        {
            return archiveRoot.EnumerateDirectories()
                .Where(d => !IsLink(d) && IsArchiveOf(d.Name, change))
                .Select(d => d.FullName)
                .Order(StringComparer.Ordinal)
                .LastOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException($"Couldn't read '{archiveRoot.FullName}': {exception.Message}", exception);
        }
    }

    /// <summary>Whether a folder name is <c>&lt;YYYY-MM-DD&gt;-&lt;change&gt;</c>, compared ordinally.</summary>
    public static bool IsArchiveOf(string folderName, string change) =>
        folderName.Length == change.Length + 11
        && ArchiveDatePrefix().IsMatch(folderName)
        && string.Equals(folderName[11..], change, StringComparison.Ordinal);

    /// <summary>
    /// The change's folder, refusing any name that could leave <c>openspec/changes/</c>. Only a single plain path
    /// segment is accepted, and the combined path is checked as well.
    /// </summary>
    public static string ResolveChangeDirectory(string repoRoot, string change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var changesRoot = ChangesRoot(repoRoot);
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

    public static string ChangesRoot(string repoRoot) => Path.GetFullPath(Path.Combine(repoRoot, "openspec", "changes"));

    // Recursive, skipping links (files and directories), so nothing outside the change's folder is read (D2).
    private static void Collect(DirectoryInfo directory, string prefix, List<(string, FileInfo)> files)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException($"Couldn't read '{directory.FullName}': {exception.Message}", exception);
        }

        foreach (var entry in entries)
        {
            if (IsLink(entry))
            {
                continue;
            }

            var relative = prefix + entry.Name;
            switch (entry)
            {
                case DirectoryInfo subdirectory:
                    Collect(subdirectory, relative + "/", files);
                    break;
                case FileInfo file when DocumentLimits.HasSyncedExtension(file.Name):
                    // The server's path rules, applied here first, so a report it would refuse is never sent.
                    if (DocumentLimits.PathProblem(relative) is { } problem)
                    {
                        throw new BridgeException($"'{relative}' can't be sent: its path {problem}. Nothing was sent.");
                    }

                    files.Add((relative, file));
                    break;
            }
        }
    }

    private static bool IsLink(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || entry.LinkTarget is not null;

    private static async Task<string> ReadUtf8Async(string fullPath, string relative, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException($"Couldn't read '{fullPath}': {exception.Message}", exception);
        }

        // A byte order mark is dropped, as File.ReadAllText did before (the strict encoding's own Preamble is empty).
        var start = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        try
        {
            return _strictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException exception)
        {
            throw new BridgeException($"'{relative}' isn't valid UTF-8, so it can't be sent: {fullPath}", exception);
        }
    }

    private static void CheckSize(string relative, long bytes)
    {
        var limit = DocumentLimits.MaxBytesFor(relative);
        if (bytes > limit)
        {
            throw new BridgeException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{relative}' is {bytes} bytes; the limit is {limit} bytes ({DocumentLimits.Describe(limit)}). Nothing was sent."));
        }
    }

    private static void CheckTotal(long total)
    {
        if (total > DocumentLimits.MaxTotalBytes)
        {
            throw new BridgeException(string.Create(
                CultureInfo.InvariantCulture,
                $"The change's documents total {total} bytes; the limit is {DocumentLimits.MaxTotalBytes} bytes ({DocumentLimits.Describe(DocumentLimits.MaxTotalBytes)}). Nothing was sent."));
        }
    }

    private static BridgeException InvalidName(string change) =>
        new($"Invalid change name '{change}': it must be a single folder name under openspec/changes/.");

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}-", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveDatePrefix();
}
