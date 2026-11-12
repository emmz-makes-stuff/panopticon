using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Git;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// agent-interaction D9; spec cli-bridge: "Reported change recorded", through the real bridge host and an in-process
// server. The link is looked up by the root git gives, as the hook will (on macOS that's the /private/var path).
public sealed class WorktreeLinkBridgeTests(PostgresFixture postgres, S3Fixture s3) : IDisposable
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";

    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-bridge-links-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ConfigPath => Path.Combine(_root, "panopticon", "config.json");

    private WorktreeLinksFile Links => WorktreeLinksFile.NextTo(new CliConfigFile(ConfigPath));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private BridgeSettings Settings(string workingDirectory, string token, string? configPath = null) =>
        new BridgeSettings("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true)
        {
            Config = new CliConfigFile(configPath ?? ConfigPath),
        };

    private static Task<string> GitRootAsync(TempGitRepo repo) => GitRepository.FindRootAsync(repo.Root, Ct);

    [Fact]
    public async Task An_accepted_report_records_its_change_for_the_working_tree()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("team-auth", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Equal("team-auth", Links.Find(await GitRootAsync(repo))?.Change);
    }

    [Fact]
    public async Task Two_worktrees_of_a_repo_each_find_their_own_change()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        var token = await server.MemberTokenAsync(Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("tags", Tasks);
        repo.Git("add", ".");
        repo.Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "tasks");
        var second = Path.Combine(_root, "second-worktree");
        repo.Git("worktree", "add", "--quiet", "-b", "change/projects", second);
        var secondTasks = Path.Combine(second, "openspec", "changes", "projects");
        Directory.CreateDirectory(secondTasks);
        await File.WriteAllTextAsync(Path.Combine(secondTasks, "tasks.md"), Tasks, Ct);

        await using (var first = await BridgeHarness.StartAsync(Settings(repo.Root, token), server.CreateClient, "claude-code", Ct))
        {
            Assert.True((await first.ReportAsync("tags", null, Ct)).IsError is not true);
        }

        await using (var other = await BridgeHarness.StartAsync(Settings(second, token), server.CreateClient, "claude-code", Ct))
        {
            Assert.True((await other.ReportAsync("projects", null, Ct)).IsError is not true);
        }

        Assert.Equal("tags", Links.Find(await GitRootAsync(repo))?.Change);
        Assert.Equal("projects", Links.Find(await GitRepository.FindRootAsync(second, Ct))?.Change);
    }

    [Fact]
    public async Task An_archived_report_clears_the_link_only_while_it_names_that_change()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        repo.WriteTasks("tags", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);
        var root = await GitRootAsync(repo);

        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);
        Assert.True((await bridge.ReportAsync("tags", null, Ct)).IsError is not true);
        Archive(repo, "team-auth");

        // The working tree has moved on to tags: archiving team-auth leaves that alone.
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);
        Assert.Equal("tags", Links.Find(root)?.Change);

        Archive(repo, "tags");
        var archived = await bridge.ReportAsync("tags", null, Ct);

        Assert.True(archived.IsError is not true, BridgeHarness.Text(archived));
        Assert.Null(Links.Find(root));
    }

    [Fact]
    public async Task A_refused_report_keeps_the_change_recorded_before()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("projects", Tasks);
        repo.WriteTasks("tags", Tasks);
        await using (var accepted = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct))
        {
            Assert.True((await accepted.ReportAsync("projects", null, Ct)).IsError is not true);
        }

        await using var refused = await BridgeHarness.StartAsync(Settings(repo.Root, "pnp_not_a_real_token"), server.CreateClient, "claude-code", Ct);
        Assert.True((await refused.ReportAsync("tags", null, Ct)).IsError);

        Assert.Equal("projects", Links.Find(await GitRootAsync(repo))?.Change);
    }

    [Fact]
    public async Task An_unwritable_config_directory_never_fails_the_report()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        var blocker = Path.Combine(_root, "blocker");
        await File.WriteAllTextAsync(blocker, "", Ct);
        var settings = Settings(repo.Root, await server.MemberTokenAsync(Ct), Path.Combine(blocker, "config.json"));
        await using var bridge = await BridgeHarness.StartAsync(settings, server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("team-auth", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Contains("worktrees.json couldn't be updated", bridge.Log, StringComparison.Ordinal);
    }

    // What /opsx:archive does: move the change's folder under archive/.
    private static void Archive(TempGitRepo repo, string change)
    {
        var archiveRoot = repo.CreateSubdirectory(Path.Combine("openspec", "changes", "archive"));
        Directory.Move(Path.Combine(repo.Root, "openspec", "changes", change), Path.Combine(archiveRoot, $"2026-11-11-{change}"));
    }
}
