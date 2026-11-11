using Panopticon.Cli.Config;
using Panopticon.Cli.Git;

namespace Panopticon.Cli.Hook;

/// <summary>The working tree a session runs in, and the change its events name (agent-interaction D9).</summary>
internal sealed record SessionChange(string WorktreeRoot, string Change)
{
    public const string BranchPrefix = "change/";

    /// <summary>
    /// The change for a session started in <paramref name="cwd"/> (spec agent-presence, A session's change), from the
    /// first of these that names one:
    /// <list type="number">
    /// <item>a <c>change/&lt;name&gt;</c> branch;</item>
    /// <item>the bridge's record for the working tree, while that change is still active in <c>openspec/changes/</c>
    /// (a stale record, from before a branch switch or an archive, is skipped);</item>
    /// <item>the active change whose <c>tasks.md</c> was modified most recently.</item>
    /// </list>
    /// Null when none does, and the hook sends nothing; null too when <paramref name="cwd"/> isn't in a git working
    /// tree, or git can't be run.
    /// </summary>
    public static async Task<SessionChange?> ResolveAsync(string cwd, WorktreeLinksFile? links, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cwd);
        string root;
        string? branch;
        try
        {
            root = await GitRepository.FindRootAsync(cwd, cancellationToken);
            branch = await GitRepository.GetBranchAsync(root, cancellationToken);
        }
        catch (BridgeException)
        {
            return null;
        }

        if (FromBranch(branch) is { } fromBranch)
        {
            return new SessionChange(root, fromBranch);
        }

        if (links?.Find(root) is { } link && ChangeDocuments.IsPlainName(link.Change) && IsActive(root, link.Change))
        {
            return new SessionChange(root, link.Change);
        }

        return NewestActive(root) is { } newest ? new SessionChange(root, newest) : null;
    }

    /// <summary>The change a branch name names: <c>change/tags</c> gives <c>tags</c>; anything else, null.</summary>
    internal static string? FromBranch(string? branch) =>
        branch is not null
        && branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
        && branch[BranchPrefix.Length..] is var name
        && ChangeDocuments.IsPlainName(name)
            ? name
            : null;

    private static bool IsActive(string root, string change) =>
        File.Exists(Path.Combine(ChangeDocuments.ChangesRoot(root), change, "tasks.md"));

    /// <summary>The active change (not under <c>archive/</c>) whose <c>tasks.md</c> changed last, or null.</summary>
    internal static string? NewestActive(string root)
    {
        try
        {
            var changes = new DirectoryInfo(ChangeDocuments.ChangesRoot(root));
            if (!changes.Exists)
            {
                return null;
            }

            return changes.EnumerateDirectories()
                .Where(d => d.Name != ChangeDocuments.ArchiveFolderName && ChangeDocuments.IsPlainName(d.Name))
                .Select(d => new FileInfo(Path.Combine(d.FullName, "tasks.md")))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Directory!.Name, StringComparer.Ordinal)
                .Select(f => f.Directory!.Name)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
