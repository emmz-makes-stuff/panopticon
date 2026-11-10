using Microsoft.EntityFrameworkCore;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// tags D8; spec cli-bridge: "Personal tags sent with reports" and "Personal tags in the config" (A malformed file),
// through the real bridge host and an in-process server.
public sealed class PersonalTagsBridgeTests(PostgresFixture postgres, S3Fixture s3) : IDisposable
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";

    // The saved token in the config file: it must never appear in an error.
    private const string FileToken = "pnp_token_in_the_file";

    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-bridge-tags-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ConfigPath => Path.Combine(_root, "panopticon", "config.json");

    private CliConfigFile Config => new(ConfigPath);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private BridgeSettings Settings(string workingDirectory, string token) =>
        new BridgeSettings("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true) { Config = Config };

    private void SaveTags(params string[] tags) => Config.Write(new CliConfig("http://localhost/", FileToken, tags));

    private static async Task<List<string>> TagsOnServerAsync(PanopticonFactory server)
    {
        await using var db = server.NewDb();
        return [.. (await db.ChangeAgentTags.Select(t => t.Key + (t.Value == null ? "" : ": " + t.Value)).ToListAsync(Ct)).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task Each_report_sends_the_tags_saved_now_and_none_saved_clears_them()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        SaveTags("dev", "Area : CLI");
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var first = await bridge.ReportAsync("walking-skeleton", null, Ct);
        Assert.True(first.IsError is not true, BridgeHarness.Text(first));
        Assert.Equal(["Area: CLI", "dev"], await TagsOnServerAsync(server));
        Assert.DoesNotContain("dev", BridgeHarness.Text(first), StringComparison.Ordinal);

        // An edit while the bridge runs counts on the next report, with no restart.
        Assert.Equal(0, new TagsCommand(Config, new StringWriter(), new StringWriter()).Run(["add", "urgent"]));
        Assert.True((await bridge.ReportAsync("walking-skeleton", null, Ct)).IsError is not true);
        Assert.Equal(["Area: CLI", "dev", "urgent"], await TagsOnServerAsync(server));

        Assert.Equal(0, new TagsCommand(Config, new StringWriter(), new StringWriter()).Run(["remove", "dev", "area: cli", "urgent"]));
        Assert.True((await bridge.ReportAsync("walking-skeleton", null, Ct)).IsError is not true);
        Assert.Empty(await TagsOnServerAsync(server));
    }

    [Fact]
    public async Task With_no_config_file_a_report_sends_no_tags()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Empty(await TagsOnServerAsync(server));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("key:")]
    public async Task A_malformed_saved_tag_is_a_tool_error_naming_the_file_and_tag_and_the_bridge_keeps_serving(string bad)
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        SaveTags("dev", bad);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var refused = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(refused.IsError);
        var text = BridgeHarness.Text(refused);
        Assert.Contains(ConfigPath, text, StringComparison.Ordinal);
        Assert.Contains($"\"{bad}\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain(FileToken, text, StringComparison.Ordinal);
        await using (var db = server.NewDb())
        {
            Assert.False(await db.Changes.AnyAsync(Ct));
        }

        SaveTags("dev");
        var fixedUp = await bridge.ReportAsync("walking-skeleton", null, Ct);
        Assert.True(fixedUp.IsError is not true, BridgeHarness.Text(fixedUp));
        Assert.Equal(["dev"], await TagsOnServerAsync(server));
    }

    [Fact]
    public async Task An_unreadable_config_file_still_reports_and_keeps_the_tags_the_server_has()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        SaveTags("dev");
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);
        Assert.True((await bridge.ReportAsync("walking-skeleton", null, Ct)).IsError is not true);

        await File.WriteAllTextAsync(ConfigPath, $$"""{ "token": "{{FileToken}}", "tags": [""", Ct);
        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Equal(["dev"], await TagsOnServerAsync(server));
        Assert.Contains(ConfigPath, bridge.Log, StringComparison.Ordinal);
        Assert.Contains("without personal tags", bridge.Log, StringComparison.Ordinal);
        Assert.DoesNotContain(FileToken, bridge.Log, StringComparison.Ordinal);
    }
}
