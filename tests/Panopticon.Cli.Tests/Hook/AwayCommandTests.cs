using System.Net;
using System.Text.Json;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Hook;
using Panopticon.Contracts;
using static Panopticon.Cli.Tests.Hook.HookCommandTests;

namespace Panopticon.Cli.Tests.Hook;

// agent-interaction D8; spec remote-answers: "Away mode", from the CLI's side, against a stub server.
public sealed class AwayCommandTests : IDisposable
{
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        _stdout.Dispose();
        _stderr.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<int> RunAsync(StubServer server, string?[] args, string? token = "pnp_away_token") =>
        new AwayCommand(() => new HttpClient(server), new BridgeSettings("https://panopticon.example/", token, "/", "vm-1"), _stdout, _stderr)
            .RunAsync([.. args.OfType<string>()], Ct);

    private static StubServer Server(bool away) =>
        new(HttpStatusCode.OK, away ? """{ "away": true, "since": "2026-11-11T19:00:00Z" }""" : """{ "away": false }""");

    [Fact]
    public async Task On_turns_away_on_and_prints_the_state()
    {
        var server = Server(away: true);

        Assert.Equal(0, await RunAsync(server, ["on"]));

        var request = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Put, "https://panopticon.example/api/agent/away", "1"), (request.Method, request.Url, request.RequestHeader));
        Assert.True(JsonSerializer.Deserialize(request.Body, ContractsJsonContext.Default.AwayState)!.Away);
        Assert.StartsWith("Away since ", _stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("wait for an answer on the dashboard", _stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", _stderr.ToString());
    }

    [Fact]
    public async Task Off_turns_away_off_and_prints_the_state()
    {
        var server = Server(away: false);

        Assert.Equal(0, await RunAsync(server, ["off"]));

        Assert.False(JsonSerializer.Deserialize(Assert.Single(server.Requests).Body, ContractsJsonContext.Default.AwayState)!.Away);
        Assert.Equal("Not away: your sessions' prompts are answered in the terminal.", _stdout.ToString().Trim());
    }

    [Fact]
    public async Task With_no_argument_it_prints_the_state_without_changing_it()
    {
        var server = Server(away: true);

        Assert.Equal(0, await RunAsync(server, []));

        var request = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Get, "https://panopticon.example/api/agent/away"), (request.Method, request.Url));
        Assert.StartsWith("Away since ", _stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_logged_in_says_how_to_log_in_and_sends_nothing()
    {
        var server = Server(away: true);

        Assert.Equal(1, await RunAsync(server, ["on"], token: null));

        Assert.Empty(server.Requests);
        Assert.Contains("panopticon login", _stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("", _stdout.ToString());
    }

    [Fact]
    public async Task A_rejected_token_is_an_error_that_never_shows_the_token()
    {
        var server = new StubServer(HttpStatusCode.Unauthorized);

        Assert.Equal(1, await RunAsync(server, ["on"]));

        Assert.Contains("refused the away switch (401)", _stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("pnp_away_token", _stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("on", "off")]
    public async Task Anything_else_is_a_usage_error(params string[] args)
    {
        var server = Server(away: true);

        Assert.Equal(1, await RunAsync(server, args));

        Assert.Empty(server.Requests);
        Assert.Equal("Usage: panopticon away [on | off]", _stderr.ToString().Trim());
    }

    [Fact]
    public void Away_with_no_time_still_reads()
    {
        Assert.StartsWith("Away: ", AwayCommand.Describe(new AwayState(true)), StringComparison.Ordinal);
    }
}
