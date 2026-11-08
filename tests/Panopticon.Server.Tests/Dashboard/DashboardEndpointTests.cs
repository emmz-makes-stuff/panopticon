using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Dashboard;

// specs/live-dashboard: One card per change (read API), Live updates (event stream), Unauthenticated in
// this change; plus the app shell served from wwwroot (design D10).
public class DashboardEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static ReportProgressArguments Report(string owner, string repo, string change, string tasks) => new(
        new RepoRef("github.com", owner, repo),
        change,
        new AgentRef("claude-code", "vm-1"),
        tasks);

    [Fact]
    public async Task Changes_lists_one_card_per_change_most_recent_first_without_signing_in()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using (var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct))
        {
            await mcp.CallToolAsync("report_progress", Report("emmz", "panopticon", "walking-skeleton", "## 1. A\n- [x] a\n- [ ] b").ToToolArguments(), cancellationToken: ct);
            await mcp.CallToolAsync("report_progress", Report("emmz", "other", "add-auth", "## 1. A\n- [ ] a").ToToolArguments(), cancellationToken: ct);
        }

        using var http = factory.CreateClient();
        var cards = await http.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, ct);

        Assert.Collection(
            cards.EnumerateArray(),
            first => Assert.Equal("add-auth", first.GetProperty("change").GetString()),
            second =>
            {
                Assert.Equal("walking-skeleton", second.GetProperty("change").GetString());
                Assert.Equal("panopticon", second.GetProperty("repo").GetProperty("name").GetString());
                Assert.Equal(1, second.GetProperty("overall").GetProperty("done").GetInt32());
                Assert.Equal(2, second.GetProperty("overall").GetProperty("total").GetInt32());
                Assert.Equal("A", second.GetProperty("currentSection").GetProperty("title").GetString());
                Assert.Equal("vm-1", second.GetProperty("agents")[0].GetProperty("machine").GetString());
            });
    }

    [Fact]
    public async Task Changes_is_an_empty_list_before_any_report()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        using var http = factory.CreateClient();

        var cards = await http.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, ct);

        Assert.Equal(0, cards.GetArrayLength());
    }

    [Fact]
    public async Task Events_stream_a_change_updated_event_after_a_report()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        var broadcaster = factory.Services.GetRequiredService<ChangeBroadcaster>();
        using var http = factory.CreateClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        // Headers may not flush until the first event, so open the stream in the background and wait
        // for its subscription before reporting.
        var responseTask = http.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        while (broadcaster.SubscriberCount == 0)
        {
            await Task.Delay(20, timeout.Token);
        }

        await using (var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct))
        {
            await mcp.CallToolAsync("report_progress", Report("emmz", "panopticon", "walking-skeleton", "## 1. A\n- [x] a").ToToolArguments(), cancellationToken: ct);
        }

        using var response = await responseTask;
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

        var lines = new List<string>();
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0 && lines.Count > 0)
            {
                break;
            }

            lines.Add(line);
        }

        Assert.Contains("event: change-updated", lines);
        Assert.Contains(lines, l => l.StartsWith("id: ", StringComparison.Ordinal));
        var data = JsonDocument.Parse(lines.Single(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..]);
        Assert.Equal("walking-skeleton", data.RootElement.GetProperty("change").GetString());
    }

    [Fact]
    public async Task Ending_the_stream_unsubscribes()
    {
        var ct = TestContext.Current.CancellationToken;
        var broadcaster = new ChangeBroadcaster();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var enumerator = DashboardEndpoints.StreamAsync(broadcaster, cancel.Token).GetAsyncEnumerator(ct);
        var moving = enumerator.MoveNextAsync();
        Assert.Equal(1, broadcaster.SubscriberCount);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
        await enumerator.DisposeAsync();

        Assert.Equal(0, broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task App_shell_is_served_at_the_root_and_for_client_routes_but_not_api_paths()
    {
        var ct = TestContext.Current.CancellationToken;
        var webRoot = Directory.CreateTempSubdirectory("panopticon-wwwroot-").FullName;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Panopticon</title>", ct);
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(ct), await s3.CreateBucketAsync(ct)) { WebRoot = webRoot };
        using var http = factory.CreateClient();

        Assert.Contains("<title>Panopticon</title>", await http.GetStringAsync("/", ct), StringComparison.Ordinal);
        Assert.Contains("<title>Panopticon</title>", await http.GetStringAsync("/changes/some-route", ct), StringComparison.Ordinal);
        using var api = await http.GetAsync("/api/does-not-exist", ct);
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
    }
}
