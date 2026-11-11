using Panopticon.Cli.Config;
using Panopticon.Cli.Git;
using Panopticon.Cli.Hook;
using Panopticon.Cli.Tests.Infrastructure;

namespace Panopticon.Cli.Tests.Hook;

// agent-interaction D9; spec agent-presence: "A session's change". The branch first, then the bridge's record while
// its change is active, then the active change with the newest tasks.md (Emmz, 2026-11-11).
public sealed class SessionChangeTests : IDisposable
{
    private const string Tasks = "## 1. Setup\n- [ ] 1.1 First\n";

    private static readonly DateTimeOffset _at = new(2026, 11, 11, 19, 0, 0, TimeSpan.Zero);

    private readonly string _configRoot = Directory.CreateTempSubdirectory("panopticon-session-change-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorktreeLinksFile Links => WorktreeLinksFile.NextTo(new CliConfigFile(Path.Combine(_configRoot, "panopticon", "config.json")));

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    private static TempGitRepo RepoOnBranch(string branch, params string[] activeChanges)
    {
        var repo = TempGitRepo.Create();
        repo.Git("checkout", "--quiet", "-b", branch);
        foreach (var change in activeChanges)
        {
            repo.WriteTasks(change, Tasks);
        }

        return repo;
    }

    private async Task RecordAsync(TempGitRepo repo, string change) =>
        Links.Record(await GitRepository.FindRootAsync(repo.Root, Ct), change, _at);

    private static void Touch(TempGitRepo repo, string change, DateTime at) =>
        File.SetLastWriteTimeUtc(Path.Combine(repo.Root, "openspec", "changes", change, "tasks.md"), at);

    [Fact]
    public async Task A_change_branch_wins_over_the_bridges_record()
    {
        // Reported tags, then switched to change/projects and started work before reporting it.
        using var repo = RepoOnBranch("change/projects", "tags", "projects");
        await RecordAsync(repo, "tags");

        var change = await SessionChange.ResolveAsync(repo.Root, Links, Ct);

        Assert.Equal("projects", change?.Change);
        Assert.Equal(await GitRepository.FindRootAsync(repo.Root, Ct), change?.WorktreeRoot);
    }

    [Fact]
    public async Task Off_a_change_branch_the_bridges_record_names_the_change()
    {
        using var repo = RepoOnBranch("main", "team-auth", "tags");
        await RecordAsync(repo, "team-auth");
        Touch(repo, "team-auth", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

        // From a subfolder too: the record is keyed by the working tree's root. It beats a newer tasks.md elsewhere.
        var change = await SessionChange.ResolveAsync(repo.CreateSubdirectory("src"), Links, Ct);

        Assert.Equal("team-auth", change?.Change);
    }

    [Fact]
    public async Task A_record_for_a_change_thats_no_longer_active_is_skipped()
    {
        using var repo = RepoOnBranch("main", "projects");
        await RecordAsync(repo, "tags");

        Assert.Equal("projects", (await SessionChange.ResolveAsync(repo.Root, Links, Ct))?.Change);
    }

    [Fact]
    public async Task With_neither_the_active_change_with_the_newest_tasks_names_it()
    {
        using var repo = RepoOnBranch("main", "tags", "projects", "team-auth");
        Touch(repo, "tags", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch(repo, "projects", new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        Touch(repo, "team-auth", new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

        // An archived change never counts, however new its tasks.md.
        repo.WriteFile("openspec/changes/archive/2026-11-11-old/tasks.md", Tasks);

        Assert.Equal("projects", (await SessionChange.ResolveAsync(repo.Root, links: null, Ct))?.Change);
    }

    [Fact]
    public async Task With_no_branch_record_or_active_change_there_is_no_change()
    {
        using var repo = RepoOnBranch("main");
        repo.WriteFile("openspec/changes/archive/2026-11-11-old/tasks.md", Tasks);

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
