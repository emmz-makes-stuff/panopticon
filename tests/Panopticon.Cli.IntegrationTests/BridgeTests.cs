using System.Text.Json;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

[assembly: AssemblyFixture(typeof(PostgresFixture))]
[assembly: AssemblyFixture(typeof(S3Fixture))]

namespace Panopticon.Cli.IntegrationTests;

// Spec cli-bridge: Stdio MCP server (tool listing), Configuration, Repo root, Agent identity, Forwarding to the server,
// and the credentials half of "Repo identity from git remote", through the real bridge host and an in-process server.
public class BridgeTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = """
        ## 1. Setup
        - [x] 1.1 First
        - [x] 1.2 Second
        ## 2. Core
        - [x] 2.1 Third
        - [ ] 2.2 Fourth
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeSettings Settings(
        string workingDirectory,
        string? serverUrl = "http://localhost/",
        string? token = PanopticonFactory.Token,
        bool tokenFromEnvironment = true) =>
        new(serverUrl, token, workingDirectory, "vm-1", tokenFromEnvironment);

    // --- Stdio MCP server ---

    [Fact]
    public async Task Agent_lists_report_progress_with_change_required_and_section_optional_and_list_projects()
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var tools = await bridge.Client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(["list_projects", "report_progress"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        var tool = tools.Single(t => t.Name == "report_progress");
        var schema = tool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("change", out _));
        Assert.True(properties.TryGetProperty("section", out _));
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["change"], required);
    }

    // --- Configuration ---

    [Fact]
    public async Task Missing_token_names_it_and_the_bridge_keeps_serving()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, token: null), () => new HttpClient(), "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        Assert.Contains("PANOPTICON_TOKEN", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Contains("panopticon login", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("PANOPTICON_SERVER_URL", BridgeHarness.Text(result), StringComparison.Ordinal);

        // Still serving.
        Assert.Equal(2, (await bridge.Client.ListToolsAsync(cancellationToken: Ct)).Count);
        Assert.True((await bridge.ReportAsync("walking-skeleton", null, Ct)).IsError);
    }

    [Fact]
    public async Task Missing_server_url_and_token_names_both()
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, serverUrl: " ", token: null), () => new HttpClient(), "claude-code", Ct);

        var text = BridgeHarness.Text(await bridge.ReportAsync("walking-skeleton", null, Ct));

        Assert.Contains("PANOPTICON_SERVER_URL", text, StringComparison.Ordinal);
        Assert.Contains("PANOPTICON_TOKEN", text, StringComparison.Ordinal);
    }

    // --- Repo root (through the tool) ---

    [Fact]
    public async Task Not_a_git_repo_is_a_tool_error()
    {
        using var directory = TempGitRepo.CreateEmptyDirectory();
        await using var bridge = await BridgeHarness.StartAsync(Settings(directory.Root), () => new HttpClient(), "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        Assert.Contains("No git repository was found", BridgeHarness.Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_origin_remote_is_a_tool_error_saying_a_remote_is_required()
    {
        using var repo = TempGitRepo.Create(originUrl: null);
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        Assert.Contains("needs a remote", BridgeHarness.Text(result), StringComparison.Ordinal);
    }

    // --- Change documents read locally (through the tool) ---

    [Fact]
    public async Task Change_missing_is_a_tool_error_naming_the_path()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var result = await bridge.ReportAsync("no-such-change", null, Ct);

        Assert.True(result.IsError);
        Assert.Contains(Path.Combine("openspec", "changes", "no-such-change"), BridgeHarness.Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Path_traversal_is_an_invalid_name_tool_error_and_nothing_is_sent()
    {
        var requests = 0;
        using var repo = TempGitRepo.Create();
        // What "../../tasks.md"-style names would reach if they weren't refused.
        File.WriteAllText(Path.Combine(repo.Root, "tasks.md"), "## 1. Outside\n- [x] secret\n");
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root),
            () => new HttpClient(new CountingHandler(() => requests++)),
            "claude-code",
            Ct);

        var result = await bridge.ReportAsync("../../etc", null, Ct);

        Assert.True(result.IsError);
        Assert.StartsWith("Invalid change name", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Equal(0, requests);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Unreadable_tasks_md_is_a_logged_tool_error_naming_the_file()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");
        Assert.SkipWhen(Environment.UserName == "root", "root reads files whatever their mode.");
        using var repo = TempGitRepo.Create();
        var tasksPath = repo.WriteTasks("walking-skeleton", Tasks);
        File.SetUnixFileMode(tasksPath, UnixFileMode.None);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root), () => new HttpClient(), "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.StartsWith("Couldn't read '", text, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("walking-skeleton", "tasks.md"), text, StringComparison.Ordinal);
        Assert.Contains("Couldn't read", bridge.Log, StringComparison.Ordinal);
    }

    private sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }

    // --- Agent identity + Forwarding: Successful report ---

    [Fact]
    public async Task Successful_report_from_a_subdirectory_returns_progress_and_identifies_the_agent()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create("git@github.com:emmz/panopticon.git");
        repo.WriteTasks("walking-skeleton", Tasks);
        var subdirectory = repo.CreateSubdirectory(Path.Combine("src", "app"));
        await using var bridge = await BridgeHarness.StartAsync(Settings(subdirectory, token: await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", 2, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Equal("Overall 3/4 · section 2 Core 1/2", BridgeHarness.Text(result));
        var progress = result.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.ReportProgressResult)!;
        Assert.Equal(new ProgressCount(3, 4), progress.Overall);
        Assert.Equal(new SectionSummary(2, "Core", new ProgressCount(1, 2)), progress.CurrentSection);

        await using var db = server.NewDb();
        var card = Assert.Single(await new ChangeCards(db).ListAsync(await Viewer.LoadAsync(db, await server.MemberIdAsync(cancellationToken: Ct), Ct), Ct));
        Assert.Equal(new CardRepo("github.com", "emmz", "panopticon"), card.Repo);
        Assert.Equal("walking-skeleton", card.Change);
        var agent = Assert.Single(card.Agents);
        Assert.Equal(("claude-code", "vm-1", "member"), (agent.Client, agent.Machine, agent.Member));
    }

    [Fact]
    public async Task Credentials_in_the_remote_are_never_sent_or_logged()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create("https://user:secret@github.com/emmz/panopticon.git");
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, token: await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        await using var db = server.NewDb();
        var card = Assert.Single(await new ChangeCards(db).ListAsync(await Viewer.LoadAsync(db, await server.MemberIdAsync(cancellationToken: Ct), Ct), Ct));
        Assert.Equal(new CardRepo("github.com", "emmz", "panopticon"), card.Repo);
        Assert.DoesNotContain("secret", bridge.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", BridgeHarness.Text(result), StringComparison.Ordinal);
    }

    // --- Forwarding: Server unreachable ---

    [Fact]
    public async Task Server_unreachable_names_the_url_and_the_bridge_keeps_serving()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        // Port 9 (discard) on loopback: nothing listens there, so the connection is refused at once.
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, serverUrl: "http://user:pw@127.0.0.1:9/?x=1"), () => new HttpClient(), "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.Contains("http://127.0.0.1:9/mcp", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pw", text, StringComparison.Ordinal);

        Assert.Equal(2, (await bridge.Client.ListToolsAsync(cancellationToken: Ct)).Count);
        Assert.True((await bridge.ReportAsync("walking-skeleton", null, Ct)).IsError);
    }

    // --- Forwarding: Token rejected ---

    [Fact]
    public async Task Token_rejected_says_so_without_echoing_the_token()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, token: "not-the-right-token"), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        // An environment token overrides the saved login, so the error names the variable to fix or remove.
        Assert.Contains("rejected the token from PANOPTICON_TOKEN", text, StringComparison.Ordinal);
        Assert.Contains("overrides the saved login", text, StringComparison.Ordinal);
        Assert.Contains("panopticon login", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-right-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-right-token", bridge.Log, StringComparison.Ordinal);

        await using var db = server.NewDb();
        Assert.Empty(await new ChangeCards(db).ListAsync(await Viewer.LoadAsync(db, await server.MemberIdAsync(cancellationToken: Ct), Ct), Ct));
    }

    [Fact]
    public async Task Saved_login_token_rejected_says_to_log_in_again()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, token: "not-the-right-token", tokenFromEnvironment: false), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.Contains("rejected the saved login's token", text, StringComparison.Ordinal);
        Assert.Contains("Run 'panopticon login'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PANOPTICON_TOKEN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-right-token", text, StringComparison.Ordinal);
    }

    // --- Forwarding: a server-side tool error comes back as a tool error ---

    [Fact]
    public async Task Server_tool_error_comes_back_as_a_tool_error()
    {
        // The bridge now applies the server's own document checks before sending (change-documents-and-history), so a
        // real server no longer refuses anything the bridge sends; a fake one stands in for a server that does.
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root),
            () => new HttpClient(new FakeMcpServer(structuredContent: "{}", toolError: "Document 'x.md' appears more than once.")),
            "claude-code",
            Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.StartsWith("The Panopticon server refused the report:", text, StringComparison.Ordinal);
        Assert.Contains("appears more than once", text, StringComparison.Ordinal);
    }

    // --- Forwarding: a malformed server result is a readable, logged tool error ---

    [Theory]
    [InlineData("""{"changeId":"c","overall":"not-a-count"}""")]
    [InlineData("""{"changeId":"c"}""")]
    public async Task Malformed_structured_result_is_a_logged_tool_error(string structuredContent)
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root), () => new HttpClient(new FakeMcpServer(structuredContent)), "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.StartsWith("The Panopticon server at http://localhost/mcp returned a malformed progress result:", text, StringComparison.Ordinal);
        Assert.Contains("malformed progress result", bridge.Log, StringComparison.Ordinal);
        Assert.DoesNotContain(PanopticonFactory.Token, bridge.Log, StringComparison.Ordinal);
    }

    /// <summary>
    /// Just enough of a Streamable HTTP MCP server, answering in plain JSON: <c>initialize</c>, notifications, and a
    /// <c>tools/call</c> whose structured content is the given JSON.
    /// </summary>
    private sealed class FakeMcpServer(string structuredContent, string? toolError = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post || request.Content is null)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed);
            }

            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var message = body.RootElement;
            if (!message.TryGetProperty("id", out var id))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
            }

            // Anything else (e.g. the SDK's server/discover probe) is "method not found", as from an older server.
            var member = message.GetProperty("method").GetString() switch
            {
                "initialize" => $$$"""
                    "result":{"protocolVersion":{{{message.GetProperty("params").GetProperty("protocolVersion").GetRawText()}}},
                     "capabilities":{"tools":{}},"serverInfo":{"name":"fake","version":"1.0.0"}}
                    """,
                "tools/call" when toolError is not null => $$"""
                    "result":{"content":[{"type":"text","text":{{JsonSerializer.Serialize(toolError)}}}],"isError":true}
                    """,
                "tools/call" => $$"""
                    "result":{"content":[{"type":"text","text":"ok"}],"structuredContent":{{structuredContent}}}
                    """,
                _ => """
                    "error":{"code":-32601,"message":"Method not found"}
                    """,
            };

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"jsonrpc":"2.0","id":{{id.GetRawText()}},{{member}}}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
