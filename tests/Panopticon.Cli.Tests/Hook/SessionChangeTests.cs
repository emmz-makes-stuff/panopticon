using Panopticon.Cli.Config;
using Panopticon.Cli.Git;
using Panopticon.Cli.Hook;
using Panopticon.Cli.Tests.Infrastructure;

namespace Panopticon.Cli.Tests.Hook;

// agent-interaction D9; spec agent-presence: "A session's change".
public sealed class SessionChangeTests : IDisposable
{
    private static readonly DateTimeOffset _at = new(2026, 11, 11, 19, 0, 0, TimeSpan.Zero);

    private readonly string _configRoot = Directory.CreateTempSubdirectory("panopticon-session-change-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorktreeLinksFile Links => WorktreeLinksFile.NextTo(new CliConfigFile(Path.Combine(_configRoot, "panopticon", "config.json")));

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    private static TempGitRepo RepoOnBranch(string branch)
    {
        var repo = TempGitRepo.Create();
        repo.Git("checkout", "--quiet", "-b", branch);
        return repo;
    }

    [Fact]
    public async Task The_bridges_link_names_the_change_on_any_branch()
    {
        using var repo = RepoOnBranch("main");
        Links.Record(await GitRepository.FindRootAsync(repo.Root, Ct), "team-auth", _at);

        // From a subfolder too: the link is keyed by the working tree's root.
        var change = await SessionChange.ResolveAsync(repo.CreateSubdirectory("src"), Links, Ct);

        Assert.Equal("team-auth", change?.Change);
    }

    [Fact]
    public async Task The_bridges_link_wins_over_a_change_branch()
    {
        using var repo = RepoOnBranch("change/tags");
        Links.Record(await GitRepository.FindRootAsync(repo.Root, Ct), "team-auth", _at);

        Assert.Equal("team-auth", (await SessionChange.ResolveAsync(repo.Root, Links, Ct))?.Change);
    }

    [Fact]
    public async Task With_no_link_the_change_branch_names_it()
    {
        using var repo = RepoOnBranch("change/tags");

        var change = await SessionChange.ResolveAsync(repo.Root, Links, Ct);

        Assert.Equal("tags", change?.Change);
        Assert.Equal(await GitRepository.FindRootAsync(repo.Root, Ct), change?.WorktreeRoot);
    }

    [Fact]
    public async Task With_no_link_and_no_change_branch_there_is_no_change()
    {
        using var repo = RepoOnBranch("main");

        Assert.Null(await SessionChange.ResolveAsync(repo.Root, Links, Ct));
        Assert.Null(await SessionChange.ResolveAsync(repo.Root, links: null, Ct));
    }

    [Fact]
    public async Task Outside_a_git_working_tree_there_is_no_change()
    {
        using var folder = TempGitRepo.CreateEmptyDirectory();

        Assert.Null(await SessionChange.ResolveAsync(folder.Root, Links, Ct));
        Assert.Null(await SessionChange.ResolveAsync(Path.Combine(folder.Root, "missing"), Links, Ct));
    }

    [Theory]
    [InlineData("change/tags", "tags")]
    [InlineData("change/team-auth", "team-auth")]
    [InlineData("change/", null)]
    [InlineData("change/a/b", null)]
    [InlineData("change/..", null)]
    [InlineData("changes/tags", null)]
    [InlineData("feature/tags", null)]
    [InlineData("main", null)]
    [InlineData(null, null)]
    public void Only_a_change_branch_with_a_plain_name_names_a_change(string? branch, string? expected)
    {
        Assert.Equal(expected, SessionChange.FromBranch(branch));
    }
}
