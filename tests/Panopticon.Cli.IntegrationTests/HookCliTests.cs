using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Hook;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// agent-interaction D1, D9; spec agent-presence: "Session events from hooks" and "Hooks never break the session", with
// the published Native AOT binary run as Claude Code runs it: the event on stdin, nothing expected on stdout.
public sealed class HookCliTests(PostgresFixture postgres, S3Fixture s3) : IDisposable
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";

    // The spec's limit for a hook that isn't waiting on a prompt.
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(5);

    // The binary's config directory ($XDG_CONFIG_HOME/panopticon): never the real one.
    private readonly string _configHome = Directory.CreateTempSubdirectory("panopticon-hook-cli-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ConfigPath => Path.Combine(_configHome, "panopticon", "config.json");

    private string HookLogText => File.Exists(Path.Combine(_configHome, "panopticon", HookLog.FileName))
        ? File.ReadAllText(Path.Combine(_configHome, "panopticon", HookLog.FileName))
        : "";

    public void Dispose() => Directory.Delete(_configHome, recursive: true);

    private static string Event(string name, string cwd, string? tool = null, JsonObject? input = null)
    {
        var json = new JsonObject { ["hook_event_name"] = name, ["session_id"] = "session-1", ["cwd"] = cwd };
        if (tool is not null)
        {
            json["tool_name"] = tool;
            json["tool_input"] = input ?? [];
        }

        return json.ToJsonString();
    }

    private sealed record HookRun(int ExitCode, string Stdout, string Stderr, TimeSpan Elapsed);

    private async Task<HookRun> RunHookAsync(string stdin, string workingDirectory, string? serverUrl = null, string? token = null)
    {
        var startInfo = new ProcessStartInfo(PublishedCli.Path)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("hook");
        startInfo.Environment.Remove(BridgeSettings.ServerUrlVariable);
        startInfo.Environment.Remove(BridgeSettings.TokenVariable);
        startInfo.Environment["XDG_CONFIG_HOME"] = _configHome;
        startInfo.Environment["APPDATA"] = _configHome;
        if (serverUrl is not null)
        {
            startInfo.Environment[BridgeSettings.ServerUrlVariable] = serverUrl;
        }

        if (token is not null)
        {
            startInfo.Environment[BridgeSettings.TokenVariable] = token;
        }

        var started = Stopwatch.StartNew();
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        await process.WaitForExitAsync(Ct);
        return new HookRun(process.ExitCode, await stdout, await stderr, started.Elapsed);
    }

    private static void AssertQuiet(HookRun run)
    {
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Stdout);
        Assert.Equal("", run.Stderr);
        Assert.True(run.Elapsed < _limit, $"The hook took {run.Elapsed}.");
    }

    [Fact]
    public async Task A_tool_starting_shows_its_activity_on_the_card_and_the_command_never_reaches_the_server()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        server.UseKestrel(0);
        server.StartServer();
        var serverUrl = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var token = await server.SeedApiTokenAsync(cancellationToken: Ct);

        // The bridge reports team-auth over HTTP, and records it for this working tree, which is on main.
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        var settings = new BridgeSettings(serverUrl, token, repo.Root, "vm-1", TokenFromEnvironment: true) { Config = new CliConfigFile(ConfigPath) };
        await using (var bridge = await BridgeHarness.StartAsync(settings, static () => new HttpClient(), "claude-code", Ct))
        {
            Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);
        }

        var input = new JsonObject { ["command"] = "curl -H 'Authorization: Bearer pnp_secret' x", ["description"] = "Run make gates" };
        var run = await RunHookAsync(Event("PreToolUse", repo.Root, "Bash", input), repo.Root, serverUrl, token);

        AssertQuiet(run);
        Assert.Equal("", HookLogText);
        await using var db = server.NewDb();
        var changeId = await db.Changes.Where(c => c.Name == "team-auth").Select(c => c.Id).SingleAsync(Ct);
        var session = Assert.Single(server.Services.GetRequiredService<PresenceStore>().For(changeId));
        Assert.Equal((SessionState.Working, "Run make gates", Environment.MachineName), (session.State, session.Activity, session.Machine));

        AssertQuiet(await RunHookAsync(Event("SessionEnd", repo.Root), repo.Root, serverUrl, token));
        Assert.Empty(server.Services.GetRequiredService<PresenceStore>().For(changeId));
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://10.255.255.1/")]
    public async Task A_server_that_cant_be_reached_still_exits_0_within_5_seconds_with_no_output(string serverUrl)
    {
        using var repo = TempGitRepo.Create();
        repo.Git("checkout", "--quiet", "-b", "change/team-auth");

        var run = await RunHookAsync(Event("PreToolUse", repo.Root, "Read"), repo.Root, serverUrl, "pnp_down_token");

        AssertQuiet(run);
        Assert.Contains("hook PreToolUse:", HookLogText, StringComparison.Ordinal);
        Assert.DoesNotContain("pnp_down_token", HookLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_login_every_hook_exits_0_with_no_output_and_sends_nothing()
    {
        using var repo = TempGitRepo.Create();
        repo.Git("checkout", "--quiet", "-b", "change/team-auth");

        foreach (var name in new[] { "PreToolUse", "PostToolUse", "Stop", "UserPromptSubmit", "SessionEnd" })
        {
            AssertQuiet(await RunHookAsync(Event(name, repo.Root, name.EndsWith("ToolUse", StringComparison.Ordinal) ? "Read" : null), repo.Root));
        }

        Assert.Contains("panopticon login", HookLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Garbage_on_stdin_exits_0_with_no_output()
    {
        using var folder = TempGitRepo.CreateEmptyDirectory();

        AssertQuiet(await RunHookAsync("this is not { json", folder.Root, "http://127.0.0.1:9/", "pnp_token"));
        Assert.Contains("isn't a Claude Code hook event", HookLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_event_it_doesnt_use_sends_nothing_and_exits_0_with_no_output()
    {
        using var repo = TempGitRepo.Create();
        repo.Git("checkout", "--quiet", "-b", "change/team-auth");

        AssertQuiet(await RunHookAsync(Event("PreCompact", repo.Root), repo.Root, "http://127.0.0.1:9/", "pnp_token"));
        Assert.Equal("", HookLogText);
    }
}
