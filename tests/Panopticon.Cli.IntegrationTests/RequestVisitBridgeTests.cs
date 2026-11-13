using System.Text.Json;
using ModelContextProtocol.Protocol;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;

namespace Panopticon.Cli.IntegrationTests;

// visit-requests D2; spec cli-bridge: "Requesting a visit" and "Visit tool instructions", through the real bridge host
// and the real MCP client, so the hook's unadvertised panopticon_answer goes through the SDK as Claude Code sends it.
public class RequestVisitBridgeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string PrUrl = "https://github.com/emmz/pan/pull/12";

    // No server and no token: the tool must answer without either.
    private static BridgeSettings Settings(string workingDirectory) =>
        new(null, null, workingDirectory, "vm-1", TokenFromEnvironment: false);

    private static Task<CallToolResult> CallAsync(BridgeHarness bridge, Dictionary<string, object?> arguments) =>
        bridge.Client.CallToolAsync(ContractsAssembly.RequestVisitToolName, arguments, cancellationToken: Ct).AsTask();

    private static Dictionary<string, object?> Arguments(string url = PrUrl, object? answer = null)
    {
        var arguments = new Dictionary<string, object?> { ["url"] = url, ["reason"] = "Review the PR", ["action"] = "Approve it if happy" };
        if (answer is not null)
        {
            arguments[RequestVisit.AnswerField] = answer;
        }

        return arguments;
    }

    private static RequestVisitResult Structured(CallToolResult result) =>
        result.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.RequestVisitResult)!;

    [Fact]
    public async Task The_tool_is_listed_with_url_and_reason_required_and_says_when_to_use_it()
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var tool = (await bridge.Client.ListToolsAsync(cancellationToken: Ct)).Single(t => t.Name == "request_visit");

        var schema = tool.ProtocolTool.InputSchema;
        Assert.Equal(["action", "reason", "url"], schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["url", "reason"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(RequestVisit.AnswerField, schema.GetRawText(), StringComparison.Ordinal);
        foreach (var use in new[] { "pull request", "pipeline run", "how a page looks" })
        {
            Assert.Contains(use, tool.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_injected_answer_reaches_the_tool_through_the_sdk_and_comes_back_with_who_answered()
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var done = await CallAsync(bridge, Arguments(answer: new Dictionary<string, object?> { ["outcome"] = "done", ["note"] = "approved", ["answeredBy"] = "Emmz" }));
        var cant = await CallAsync(bridge, Arguments(answer: new Dictionary<string, object?> { ["outcome"] = "cant", ["answeredBy"] = "Bob" }));

        Assert.True(done.IsError is not true, BridgeHarness.Text(done));
        Assert.Equal("Done: approved. Answered by Emmz via Panopticon.", BridgeHarness.Text(done));
        Assert.Equal(new RequestVisitResult(true, PrUrl, "done", "approved", "Emmz"), Structured(done));
        Assert.Equal("Can't. Answered by Bob via Panopticon.", BridgeHarness.Text(cant));
        Assert.Equal(new RequestVisitResult(true, PrUrl, "cant", null, "Bob"), Structured(cant));
    }

    [Fact]
    public async Task With_no_answer_or_a_malformed_one_it_returns_at_once_saying_ask_in_the_terminal_with_the_url()
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        foreach (var arguments in new[]
        {
            Arguments(),
            Arguments(answer: "done"),
            Arguments(answer: new Dictionary<string, object?> { ["outcome"] = "maybe", ["answeredBy"] = "Emmz" }),
            Arguments(answer: new Dictionary<string, object?> { ["outcome"] = "done" }),
        })
        {
            var result = await CallAsync(bridge, arguments);

            Assert.True(result.IsError is not true, BridgeHarness.Text(result));
            Assert.Equal(new RequestVisitResult(false, PrUrl), Structured(result));
            Assert.Contains("Ask them in the terminal", BridgeHarness.Text(result), StringComparison.Ordinal);
            Assert.EndsWith(PrUrl, BridgeHarness.Text(result), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("file:///Users/emmz/report.html", "only absolute http and https URLs can be visited")]
    [InlineData("https://bob:secret@example.com/", "the url has a user name or password in it")]
    public async Task A_url_over_the_limits_is_a_tool_error_naming_the_problem(string url, string problem)
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var result = await CallAsync(bridge, Arguments(url, new Dictionary<string, object?> { ["outcome"] = "done", ["answeredBy"] = "Emmz" }));

        Assert.True(result.IsError);
        Assert.Equal($"This visit can't be requested: {problem}.", BridgeHarness.Text(result));
    }
}
