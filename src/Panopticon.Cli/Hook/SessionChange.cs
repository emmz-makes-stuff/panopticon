using Panopticon.Cli.Config;
using Panopticon.Cli.Git;

namespace Panopticon.Cli.Hook;

/// <summary>The working tree a session runs in, and the change its events name (agent-interaction D9).</summary>
internal sealed record SessionChange(string WorktreeRoot, string Change)
{
    public const string BranchPrefix = "change/";

    /// <summary>
    /// The change for a session started in <paramref name="cwd"/> (spec agent-presence, A session's change): the
    /// bridge's link for its working tree, else a <c>change/&lt;name&gt;</c> branch, else null, and the hook sends
    /// nothing. Null too when <paramref name="cwd"/> isn't in a git working tree, or git can't be run.
    /// </summary>
    public static async Task<SessionChange?> ResolveAsync(string cwd, WorktreeLinksFile? links, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cwd);
        string root;
        try
        {
            root = await GitRepository.FindRootAsync(cwd, cancellationToken);
        }
        catch (BridgeException)
        {
            return null;
        }

        if (links?.Find(root) is { } link && ChangeDocuments.IsPlainName(link.Change))
        {
            return new SessionChange(root, link.Change);
        }

        string? branch;
        try
        {
            branch = await GitRepository.GetBranchAsync(root, cancellationToken);
        }
        catch (BridgeException)
        {
            return null;
        }

        return FromBranch(branch) is { } change ? new SessionChange(root, change) : null;
    }

    /// <summary>The change a branch name names: <c>change/tags</c> gives <c>tags</c>; anything else, null.</summary>
    internal static string? FromBranch(string? branch) =>
        branch is not null
        && branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
        && branch[BranchPrefix.Length..] is var name
        && ChangeDocuments.IsPlainName(name)
            ? name
            : null;
}
