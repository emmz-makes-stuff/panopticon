using System.Text.Json;

namespace Panopticon.Cli.Config;

/// <summary>The change a working tree last reported, and when.</summary>
internal sealed record WorktreeLink(string Change, DateTimeOffset At);

/// <summary>
/// <c>worktrees.json</c>, next to the CLI config (agent-interaction D9): each working tree's root, as
/// <c>git rev-parse --show-toplevel</c> gives it, to the change the bridge last reported from it. The bridge writes it
/// after each accepted report and <c>panopticon hook</c> reads it, so a session's events name its change.
/// <para>
/// It's a cache the next report rebuilds, not a record anyone depends on: a file that can't be read counts as empty,
/// and is replaced on the next write. Writes go through <see cref="UserOnlyFile"/>, like the config.
/// </para>
/// </summary>
internal sealed class WorktreeLinksFile(string path)
{
    public const string FileName = "worktrees.json";

    public string Path { get; } = path;

    /// <summary>The link file in the same directory as <paramref name="config"/>.</summary>
    public static WorktreeLinksFile NextTo(CliConfigFile config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(config.Path))!;
        return new(System.IO.Path.Combine(directory, FileName));
    }

    /// <summary>The working tree's link, or null when it has none or the file can't be read.</summary>
    public WorktreeLink? Find(string worktreeRoot)
    {
        ArgumentNullException.ThrowIfNull(worktreeRoot);
        return Load().GetValueOrDefault(Key(worktreeRoot));
    }

    /// <summary>Links the working tree to <paramref name="change"/>, replacing what it named before.</summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its directory isn't writable.</exception>
    public void Record(string worktreeRoot, string change, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(worktreeRoot);
        ArgumentNullException.ThrowIfNull(change);
        var links = Load();
        links[Key(worktreeRoot)] = new WorktreeLink(change, at);
        Save(links);
    }

    /// <summary>
    /// Removes the working tree's link if it still names <paramref name="change"/> (it was archived). A link to
    /// another change is left alone: the working tree has moved on.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its directory isn't writable.</exception>
    public void Clear(string worktreeRoot, string change)
    {
        ArgumentNullException.ThrowIfNull(worktreeRoot);
        ArgumentNullException.ThrowIfNull(change);
        var links = Load();
        if (links.TryGetValue(Key(worktreeRoot), out var link) && string.Equals(link.Change, change, StringComparison.Ordinal))
        {
            links.Remove(Key(worktreeRoot));
            Save(links);
        }
    }

    private static string Key(string worktreeRoot) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(worktreeRoot));

    private Dictionary<string, WorktreeLink> Load()
    {
        try
        {
            using var stream = File.OpenRead(Path);
            var links = JsonSerializer.Deserialize(stream, CliJsonContext.Default.DictionaryStringWorktreeLink);
            // An entry with no change (hand-edited, or from a later version) is dropped rather than trusted.
            return (links ?? []).Where(entry => entry.Value is { Change.Length: > 0 })
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, WorktreeLink> links) =>
        UserOnlyFile.Write(Path, stream => JsonSerializer.Serialize(stream, links, CliJsonContext.Default.DictionaryStringWorktreeLink));
}
