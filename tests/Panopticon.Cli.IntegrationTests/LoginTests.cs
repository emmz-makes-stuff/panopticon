using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Login;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// specs/reporter-credentials: "Starting a device login", "Approving a device login" (the CLI side), "CLI saves its
// credential"; design D15. The CLI runs in-process against a test server. Its waits advance the server's clock instead
// of sleeping, so polling and the ten-minute expiry take no real time.
public sealed partial class LoginTests(PostgresFixture postgres, S3Fixture s3) : IDisposable
{
    private const string ServerUrl = "http://localhost/";

    private readonly string _configRoot = Directory.CreateTempSubdirectory("panopticon-login-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CliConfigFile Config => new(Path.Combine(_configRoot, "panopticon", "config.json"));

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    [GeneratedRegex("[A-Z]{4}-[A-Z]{4}")]
    private static partial Regex CodePattern();

    private sealed record Run(int ExitCode, string Stdout, string Stderr);

    private async Task<(PanopticonFactory Server, ManualClock Clock)> ServerAsync()
    {
        var clock = ManualClock.StartingAt2026();
        var server = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct)) { Clock = clock };
        return (server, clock);
    }

    /// <summary>
    /// Runs <c>panopticon login</c>. <paramref name="onFirstWait"/> runs once, after the code is printed, with that
    /// code: the moment a person would approve or deny it.
    /// </summary>
    private async Task<Run> LoginAsync(
        PanopticonFactory server,
        ManualClock clock,
        string[] args,
        Func<string, Task>? onFirstWait = null,
        Dictionary<string, string?>? environment = null,
        CancellationToken? cancellationToken = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var waited = false;
        async Task Delay(TimeSpan interval, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!waited && onFirstWait is not null)
            {
                waited = true;
                await onFirstWait(CodePattern().Match(stdout.ToString()).Value);
            }

            clock.Advance(interval);
        }

        var command = new LoginCommand(
            server.CreateClient,
            name => environment?.GetValueOrDefault(name),
            Config,
            "vm-1",
            Delay,
            stdout,
            stderr);
        var exitCode = await command.RunAsync(args, cancellationToken ?? Ct);
        return new Run(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static Func<string, Task> Decide(HttpClient browser, string decision) => async code =>
        (await browser.PostApiAsync($"{DeviceEndpoints.LoginPath(code)}/{decision}", null, Ct)).EnsureSuccessStatusCode().Dispose();

    // "Login on a VM", "Approve" (the CLI saves the token and prints the member), and "Saved for the bridge".
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Login_prints_the_code_and_url_and_on_approval_saves_a_token_the_bridge_reports_with()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);

        var run = await LoginAsync(server, clock, ["--server", ServerUrl], Decide(browser, "approve"));

        Assert.Equal(0, run.ExitCode);
        var code = CodePattern().Match(run.Stdout).Value;
        Assert.Contains($"{PanopticonFactory.PublicUrl}device?code={code}", run.Stdout, StringComparison.Ordinal);
        Assert.Contains("vm-1", run.Stdout, StringComparison.Ordinal);
        Assert.Contains($"as member ({PanopticonFactory.MemberEmail})", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(run.Stderr);

        // Saved for the user only, and the bridge uses it with no environment variables.
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Config.Path));
        var saved = Config.Read(new StringWriter())!;
        Assert.Equal(ServerUrl, saved.ServerUrl);
        Assert.DoesNotContain(saved.Token!, run.Stdout, StringComparison.Ordinal);
        using var repo = TempGitRepo.Create("git@github.com:emmz/panopticon.git");
        repo.WriteTasks("team-auth", "- [x] 1.1\n- [ ] 1.2\n");
        var settings = BridgeSettings.Resolve(_ => null, Config, new StringWriter(), repo.Root, "vm-1");
        await using (var bridge = await BridgeHarness.StartAsync(settings, server.CreateClient, "claude-code", Ct))
        {
            var result = await bridge.ReportAsync("team-auth", null, Ct);
            Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        }

        var card = Assert.Single((await browser.GetFromJsonAsync<List<ChangeCard>>(DashboardEndpoints.ChangesPath, Ct))!);
        Assert.Equal("member", Assert.Single(card.Agents).Member);
    }

    [Fact]
    public async Task Logging_in_again_replaces_the_saved_login()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);
        Config.Write(new SavedLogin("https://old.example.com/", "pnp_old"));

        var run = await LoginAsync(server, clock, ["--server", ServerUrl], Decide(browser, "approve"));

        Assert.Equal(0, run.ExitCode);
        var saved = Config.Read(new StringWriter())!;
        Assert.Equal(ServerUrl, saved.ServerUrl);
        Assert.NotEqual("pnp_old", saved.Token);
    }

    // "Deny".
    [Fact]
    public async Task Deny_stops_with_a_message_exits_non_zero_and_saves_nothing()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);

        var run = await LoginAsync(server, clock, ["--server", ServerUrl], Decide(browser, "deny"));

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("denied", run.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Config.Path));
        await using var db = server.NewDb();
        Assert.Equal(0, await db.ApiTokens.CountAsync(Ct));
    }

    // "Nobody approves".
    [Fact]
    public async Task Nobody_approves_and_the_cli_says_the_code_expired_and_to_log_in_again()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;
        var started = clock.GetUtcNow();

        var run = await LoginAsync(server, clock, ["--server", ServerUrl]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("expired", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("panopticon login", run.Stderr, StringComparison.Ordinal);
        Assert.True(clock.GetUtcNow() - started >= DeviceEndpoints.Lifetime);
        Assert.False(File.Exists(Config.Path));
    }

    [Fact]
    public async Task The_server_comes_from_the_environment_then_the_saved_login()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);

        var fromEnvironment = await LoginAsync(
            server, clock, [], Decide(browser, "approve"), new() { [BridgeSettings.ServerUrlVariable] = ServerUrl });
        clock.Advance(TimeSpan.FromMinutes(1));
        var fromSaved = await LoginAsync(server, clock, [], Decide(browser, "approve"));

        Assert.Equal(0, fromEnvironment.ExitCode);
        Assert.Equal(0, fromSaved.ExitCode);
        Assert.Equal(ServerUrl, Config.Read(new StringWriter())!.ServerUrl);
    }

    [Fact]
    public async Task With_no_server_anywhere_it_says_to_pass_server()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;

        var run = await LoginAsync(server, clock, []);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--server", run.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--server")]
    [InlineData("--bogus")]
    public async Task Bad_arguments_are_a_usage_error(string argument)
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;

        var run = await LoginAsync(server, clock, [argument]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("panopticon login", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_while_waiting_exits_130_and_saves_nothing()
    {
        var (server, clock) = await ServerAsync();
        await using var _ = server;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = await LoginAsync(server, clock, ["--server", ServerUrl], _ => cancel.CancelAsync(), cancellationToken: cancel.Token);

        Assert.Equal(LoginCommand.Cancelled, run.ExitCode);
        Assert.False(File.Exists(Config.Path));
    }

    [Fact]
    public async Task An_unreachable_server_is_named_and_exits_non_zero()
    {
        var stderr = new StringWriter();
        var command = new LoginCommand(() => new HttpClient(), _ => null, Config, "vm-1", (_, _) => Task.CompletedTask, new StringWriter(), stderr);

        // Port 9 (discard) on loopback: nothing listens there.
        var exitCode = await command.RunAsync(["--server", "http://127.0.0.1:9/"], Ct);

        Assert.Equal(1, exitCode);
        Assert.Contains("http://127.0.0.1:9/", stderr.ToString(), StringComparison.Ordinal);
    }
}
