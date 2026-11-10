using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Contracts;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Metrics;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// projects D9; spec change-filters: "Filter by Project", "Project filter choices"; the Project on cards, history rows
// and change pages.
public class ProjectFilterTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string KitEmail = "kit@example.com";

    private const string Tasks = "## 1. Work\n- [x] 1.1\n- [ ] 1.2\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cards_history_rows_and_change_pages_carry_the_Project_or_say_personal()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        var cards = (await world.Emmz.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct)).EnumerateArray()
            .ToDictionary(c => c.GetProperty("change").GetString()!);
        AssertProject(cards["pan-live"], world.Pan, "PAN");
        AssertPersonal(cards["scratch"]);

        var history = (await world.Emmz.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, Ct)).GetProperty("changes").EnumerateArray()
            .ToDictionary(c => c.GetProperty("change").GetString()!);
        AssertProject(history["acme-done"], world.Acme, "ACME");
        AssertPersonal(history["scratch-done"]);

        var id = history["pan-done"].GetProperty("changeId").GetGuid();
        var page = await world.Emmz.GetFromJsonAsync<JsonElement>(ChangeEndpoints.ChangePath(id), Ct);
        AssertProject(page.GetProperty("card"), world.Pan, "PAN");
    }

    [Fact]
    public async Task The_history_and_metrics_filter_by_a_Project_or_by_personal()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        Assert.Equal(["pan-done"], await HistoryAsync(world.Emmz, world.Pan.ToString()));
        Assert.Equal(["scratch-done"], await HistoryAsync(world.Emmz, "personal"));
        Assert.Equal(["kit-done"], await HistoryAsync(world.Kit, "Personal"));
        Assert.Equal(1, await MetricsCountAsync(world.Emmz, world.Acme.ToString()));
        Assert.Equal(1, await MetricsCountAsync(world.Emmz, "personal"));
        Assert.Equal(3, await MetricsCountAsync(world.Emmz, null));
    }

    [Fact]
    public async Task A_Project_the_viewer_isnt_in_matches_nothing()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        Assert.Empty(await HistoryAsync(world.Kit, world.Acme.ToString()));
        Assert.Equal(0, await MetricsCountAsync(world.Kit, world.Acme.ToString()));
    }

    [Theory]
    [InlineData("/api/history?project=PAN")]
    [InlineData("/api/metrics?project=PAN")]
    public async Task A_project_that_isnt_an_id_or_personal_is_a_400_naming_it(string path)
    {
        await using var world = await World.CreateAsync(postgres, s3);

        using var response = await world.Emmz.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("project must be a Project id or personal", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_history_offers_the_viewers_Projects_with_archived_changes_and_personal_when_they_have_one()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        var emmz = await world.Emmz.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryFiltersPath, Ct);
        var kit = await world.Kit.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryFiltersPath, Ct);

        // ZED has no archived change, and ACME isn't Kit's.
        Assert.Equal(["ACME", "PAN"], Codes(emmz));
        Assert.True(emmz.GetProperty("personal").GetBoolean());
        Assert.Equal(["PAN"], Codes(kit));
        Assert.Equal(world.Pan, kit.GetProperty("projects")[0].GetProperty("id").GetGuid());
        Assert.True(kit.GetProperty("personal").GetBoolean());
    }

    [Fact]
    public async Task Personal_isnt_offered_to_a_viewer_with_no_archived_personal_change()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        await using (var client = await factory.CreateMcpClientAsync(Ct))
        {
            await World.ReportAsync(client, World.Report("pan-done", pan, archived: true));
        }

        var choices = await browser.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryFiltersPath, Ct);

        Assert.False(choices.GetProperty("personal").GetBoolean());
    }

    private static void AssertProject(JsonElement card, Guid id, string code)
    {
        var project = card.GetProperty("project");
        Assert.Equal((id, code, code), (project.GetProperty("id").GetGuid(), project.GetProperty("code").GetString(), project.GetProperty("name").GetString()));
        Assert.False(card.GetProperty("personal").GetBoolean());
    }

    private static void AssertPersonal(JsonElement card)
    {
        Assert.False(card.TryGetProperty("project", out var project) && project.ValueKind != JsonValueKind.Null);
        Assert.True(card.GetProperty("personal").GetBoolean());
    }

    private static List<string> Codes(JsonElement choices) =>
        [.. choices.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("code").GetString()!)];

    private static async Task<List<string>> HistoryAsync(HttpClient browser, string project) =>
        [.. (await browser.GetFromJsonAsync<JsonElement>($"{ChangeEndpoints.HistoryPath}?project={project}", Ct)).GetProperty("changes")
            .EnumerateArray().Select(c => c.GetProperty("change").GetString()!).Order(StringComparer.Ordinal)];

    private static async Task<int> MetricsCountAsync(HttpClient browser, string? project) =>
        (await browser.GetFromJsonAsync<JsonElement>(project is null ? MetricsEndpoints.MetricsPath : $"{MetricsEndpoints.MetricsPath}?project={project}", Ct))
            .GetProperty("changes").GetInt32();

    /// <summary>
    /// Emmz owns <c>PAN</c>, <c>ACME</c> and <c>ZED</c>; Kit is a viewer of <c>PAN</c>. Emmz reported <c>pan-live</c>
    /// (active) and <c>pan-done</c> in <c>PAN</c>, <c>acme-done</c> in <c>ACME</c>, and her own <c>scratch</c> (active)
    /// and <c>scratch-done</c>; Kit reported their own <c>kit-done</c>. The <c>-done</c> ones are archived.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        public required PanopticonFactory Factory { get; init; }

        public required HttpClient Emmz { get; init; }

        public required HttpClient Kit { get; init; }

        public required Guid Pan { get; init; }

        public required Guid Acme { get; init; }

        public static async Task<World> CreateAsync(PostgresFixture postgres, S3Fixture s3)
        {
            var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
            var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
            var pan = await emmz.CreateProjectAsync("PAN", Ct);
            var acme = await emmz.CreateProjectAsync("ACME", Ct);
            await emmz.CreateProjectAsync("ZED", Ct);
            await emmz.InviteToProjectAsync(pan, KitEmail, "viewer", Ct);
            var kit = await factory.CreateSignedInClientAsync(KitEmail, Ct);

            await using (var client = await factory.CreateMcpClientAsync(Ct))
            {
                await ReportAsync(client, Report("pan-live", pan));
                await ReportAsync(client, Report("pan-done", pan, archived: true));
                await ReportAsync(client, Report("acme-done", acme, archived: true));
                await ReportAsync(client, Report("scratch", null));
                await ReportAsync(client, Report("scratch-done", null, archived: true));
            }

            await using (var client = await factory.CreateMcpClientAsync(await factory.CreateApiTokenAsync(KitEmail, cancellationToken: Ct), Ct))
            {
                await ReportAsync(client, Report("kit-done", null, archived: true));
            }

            return new World { Factory = factory, Emmz = emmz, Kit = kit, Pan = pan, Acme = acme };
        }

        public static ReportProgressArguments Report(string change, Guid? project, bool archived = false) =>
            new(new RepoRef("github.com", "emmz", "panopticon"), change, new AgentRef("claude-code", "vm-1"), Tasks)
            {
                Project = project?.ToString(),
                Archive = archived ? $"2026-11-08-{change}" : null,
            };

        public static async Task ReportAsync(ModelContextProtocol.Client.McpClient client, ReportProgressArguments report)
        {
            var result = await client.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct);
            Assert.True(result.IsError is not true, string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text)));
        }

        public async ValueTask DisposeAsync()
        {
            Emmz.Dispose();
            Kit.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
