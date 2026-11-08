using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;
using Panopticon.Server.Live;
using Panopticon.Server.Progress;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Mcp;

// specs/progress-reporting: Online MCP endpoint, Bearer token required, Report content, Change updates published.
public class ReportProgressEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = """
        ## 1. Setup
        - [x] 1.1
        - [x] 1.2
        ## 2. Core
        - [x] 2.1
        - [ ] 2.2
        - [ ] 2.3
        """;

    private static readonly ReportProgressArguments _report = new(
        new RepoRef("github.com", "emmz", "panopticon"),
        "walking-skeleton",
        new AgentRef("claude-code", "vm-1"),
        Tasks);

    // --- Requirement: Online MCP endpoint ---

    [Fact]
    public async Task Tool_is_discoverable_with_its_input_schema()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);

        var tools = await client.ListToolsAsync(cancellationToken: ct);

        var tool = Assert.Single(tools, t => t.Name == "report_progress");
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        foreach (var name in new[] { "repo", "change", "agent", "tasksMarkdown", "section" })
        {
            Assert.True(properties.TryGetProperty(name, out _), $"schema lacks '{name}'");
        }
    }

    // --- Requirement: Bearer token required ---

    [Fact]
    public async Task Missing_token_is_401_and_records_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);

        using var response = await PostToolCallAsync(factory, authorization: null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecordedAsync(factory, ct);
    }

    [Fact]
    public async Task Wrong_token_is_401_and_records_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);

        using var response = await PostToolCallAsync(factory, new AuthenticationHeaderValue("Bearer", "not-the-token"), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("not-the-token", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        await AssertNothingRecordedAsync(factory, ct);
    }

    [Fact]
    public async Task No_token_configured_fails_at_startup_naming_the_setting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct, bootstrapToken: null);

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Panopticon:BootstrapToken", ex.Message, StringComparison.Ordinal);
    }

    // --- Requirement: Report content ---

    [Fact]
    public async Task Valid_report_returns_computed_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);

        var result = await client.CallToolAsync("report_progress", _report.ToToolArguments(), cancellationToken: ct);

        Assert.NotEqual(true, result.IsError);
        var progress = result.StructuredContent?.Deserialize(ContractsJsonContext.Default.ReportProgressResult);
        Assert.NotNull(progress);
        Assert.Equal(new ProgressCount(3, 5), progress.Overall);
        Assert.Equal(new SectionSummary(2, "Core", new ProgressCount(1, 3)), progress.CurrentSection);
        Assert.Equal("Overall 3/5 · section 2 Core 1/3", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Missing_required_fields_are_named_and_nothing_is_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var arguments = _report.ToToolArguments();
        arguments.Remove("repo");
        arguments.Remove("tasksMarkdown");
        arguments["change"] = JsonSerializer.SerializeToElement("   ", ContractsJsonContext.Default.String);

        var result = await client.CallToolAsync("report_progress", arguments, cancellationToken: ct);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        foreach (var field in new[] { "repo.host", "repo.owner", "repo.name", "change", "tasksMarkdown" })
        {
            Assert.Contains(field, text, StringComparison.Ordinal);
        }

        await AssertNothingRecordedAsync(factory, ct);
    }

    [Fact]
    public async Task Oversized_tasks_is_a_tool_error_stating_the_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var oversized = _report with { TasksMarkdown = new string('a', TasksParser.MaxBytes + 1) };

        var result = await client.CallToolAsync("report_progress", oversized.ToToolArguments(), cancellationToken: ct);

        Assert.True(result.IsError);
        Assert.Contains("1 MiB", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        await AssertNothingRecordedAsync(factory, ct);
    }

    [Fact]
    public async Task Fields_are_trimmed_before_recording()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var padded = _report with { Change = "  walking-skeleton  ", Agent = new AgentRef(" claude-code ", " vm-1 ") };

        await client.CallToolAsync("report_progress", padded.ToToolArguments(), cancellationToken: ct);

        await using var db = factory.NewDb();
        Assert.Equal("walking-skeleton", (await db.Changes.SingleAsync(ct)).Name);
        var agent = await db.Agents.SingleAsync(ct);
        Assert.Equal(("claude-code", "vm-1"), (agent.ClientName, agent.MachineName));
    }

    // --- Requirement: Change updates published ---

    [Fact]
    public async Task Accepted_report_publishes_the_updated_card()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);
        using var subscription = factory.Services.GetRequiredService<ChangeBroadcaster>().Subscribe();

        await client.CallToolAsync("report_progress", (_report with { Section = 1 }).ToToolArguments(), cancellationToken: ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var card = await subscription.Reader.ReadAsync(timeout.Token);
        Assert.Equal("walking-skeleton", card.Change);
        Assert.Equal(("github.com", "emmz", "panopticon"), (card.Repo.Host, card.Repo.Owner, card.Repo.Name));
        Assert.Equal((3, 5), (card.Overall.Done, card.Overall.Total));
        Assert.Equal(1, card.CurrentSection?.Number);
        var agent = Assert.Single(card.Agents);
        Assert.Equal(("claude-code", "vm-1"), (agent.Client, agent.Machine));
    }

    private static async Task<HttpResponseMessage> PostToolCallAsync(
        PanopticonFactory factory,
        AuthenticationHeaderValue? authorization,
        CancellationToken ct)
    {
        using var http = factory.CreateClient();
        var body = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"report_progress","arguments":{
            "repo":{"host":"github.com","owner":"emmz","name":"panopticon"},"change":"walking-skeleton",
            "agent":{"client":"claude-code","machine":"vm-1"},"tasksMarkdown":"- [x] a"}}}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, ServerSetup.McpPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = authorization;
        return await http.SendAsync(request, ct);
    }

    private static async Task AssertNothingRecordedAsync(PanopticonFactory factory, CancellationToken ct)
    {
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.Changes.CountAsync(ct));
        Assert.Equal(0, await db.ReportEvents.CountAsync(ct));
    }
}
