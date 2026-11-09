using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using Panopticon.Contracts;
using Panopticon.Server.Data;
using Panopticon.Server.Metrics;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Metrics;

// change-metrics D4; spec: Time to archive, Change page metrics (API side), Metrics page, Metrics page filters.
public class MetricsEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Open = "## 1. Only\n- [ ] 1.1 a\n";
    private const string Ticked = "## 1. Only\n- [x] 1.1 a\n";
    private static readonly RepoRef _panopticon = new("github.com", "emmz", "panopticon");
    private static readonly RepoRef _meko = new("github.com", "emmz", "meko");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- GET /api/changes/{id}/metrics ---

    [Fact]
    public async Task An_archived_change_took_its_first_report_to_its_archive_with_each_sections_time()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: Ct);
        const string start = "## 1. Server\n- [ ] 1.1 a\n## 2. Web\n- [ ] 2.1 b\n";
        const string oneDone = "## 1. Server\n- [x] 1.1 a\n## 2. Web\n- [ ] 2.1 b\n";
        const string allDone = "## 1. Server\n- [x] 1.1 a\n## 2. Web\n- [x] 2.1 b\n";
        var first = clock.GetUtcNow();
        var id = await ReportAsync(mcp, _panopticon, "team-auth", start, section: 1);
        clock.Advance(TimeSpan.FromHours(2));
        await ReportAsync(mcp, _panopticon, "team-auth", oneDone, section: 2);
        clock.Advance(new TimeSpan(3, 30, 0));
        await ReportAsync(mcp, _panopticon, "team-auth", allDone, section: 2);
        clock.Advance(new TimeSpan(5, 0, 30, 0));
        await ReportAsync(mcp, _panopticon, "team-auth", allDone, section: null, archive: "2026-11-08-team-auth");
        var archived = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromDays(1));

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>(MetricsEndpoints.ChangeMetricsPath(id), Ct);

        Assert.Equal(first, metrics.GetProperty("firstReportedAt").GetDateTimeOffset());
        Assert.Equal(archived, metrics.GetProperty("archivedAt").GetDateTimeOffset());
        Assert.Equal((long)(archived - first).TotalSeconds, metrics.GetProperty("durationSeconds").GetInt64());
        Assert.False(metrics.GetProperty("active").GetBoolean());
        Assert.Collection(
            metrics.GetProperty("sections").EnumerateArray(),
            s => Assert.Equal((1, "Server", 2 * 3600L, false), Section(s)),
            s => Assert.Equal((2, "Web", 3 * 3600L + 1800, false), Section(s)));
    }

    [Fact]
    public async Task An_active_change_has_its_time_so_far_and_a_section_in_progress()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: Ct);
        var id = await ReportAsync(mcp, _panopticon, "filters-and-idle-cards", Open, section: 1);
        clock.Advance(TimeSpan.FromDays(2));

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>(MetricsEndpoints.ChangeMetricsPath(id), Ct);

        Assert.True(metrics.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("archivedAt").ValueKind);
        Assert.Equal(2 * 86400L, metrics.GetProperty("durationSeconds").GetInt64());
        var section = Assert.Single(metrics.GetProperty("sections").EnumerateArray());
        Assert.Equal((1, "Only", 2 * 86400L, true), Section(section));
        Assert.Equal(JsonValueKind.Null, section.GetProperty("finishedAt").ValueKind);
    }

    [Fact]
    public async Task A_section_left_unfinished_at_the_archive_stops_counting_there()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: Ct);
        var id = await ReportAsync(mcp, _panopticon, "abandoned", Open, section: 1);
        clock.Advance(TimeSpan.FromHours(4));
        await ReportAsync(mcp, _panopticon, "abandoned", Open, section: null, archive: "2026-11-08-abandoned");
        clock.Advance(TimeSpan.FromDays(3));

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>(MetricsEndpoints.ChangeMetricsPath(id), Ct);

        Assert.Equal(4 * 3600L, metrics.GetProperty("durationSeconds").GetInt64());
        Assert.Equal((1, "Only", 4 * 3600L, true), Section(Assert.Single(metrics.GetProperty("sections").EnumerateArray())));
    }

    [Fact]
    public async Task An_unknown_change_is_404()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        var response = await http.GetAsync(MetricsEndpoints.ChangeMetricsPath(Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signed_out_the_metrics_api_answers_401()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(MetricsEndpoints.ChangeMetricsPath(Guid.NewGuid()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(MetricsEndpoints.MetricsPath, Ct)).StatusCode);
    }

    // --- GET /api/metrics ---

    [Fact]
    public async Task Team_metrics_count_archived_changes_with_medians_weeks_and_the_slowest_sections()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        var ids = await SeedTeamAsync(factory, clock);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>(MetricsEndpoints.MetricsPath, Ct);

        // Archives: alpha 2h, beta 5h (week of 2 Nov), gamma 10h (week of 9 Nov). Sections: alpha 1h, beta 3h, gamma 2h.
        Assert.Equal(3, metrics.GetProperty("changes").GetInt32());
        Assert.Equal(5 * 3600L, metrics.GetProperty("medianArchiveSeconds").GetInt64());
        Assert.Equal(2 * 3600L, metrics.GetProperty("medianSectionSeconds").GetInt64());

        var weeks = metrics.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal(MetricsReader.WeekCount, weeks.Count);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero), weeks[0].GetProperty("weekStart").GetDateTimeOffset());
        Assert.All(weeks[..10], w => Assert.Equal((0, JsonValueKind.Null), (w.GetProperty("changes").GetInt32(), w.GetProperty("medianArchiveSeconds").ValueKind)));
        Assert.Equal((new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), 2, 12600L), Week(weeks[10]));
        Assert.Equal((new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), 1, 10 * 3600L), Week(weeks[11]));

        Assert.Collection(
            metrics.GetProperty("slowestSections").EnumerateArray(),
            s => Assert.Equal((ids["beta"], "beta", "panopticon", 1, "Only", 3 * 3600L), Slow(s)),
            s => Assert.Equal((ids["gamma"], "gamma", "meko", 1, "Only", 2 * 3600L), Slow(s)),
            s => Assert.Equal((ids["alpha"], "alpha", "panopticon", 1, "Only", 3600L), Slow(s)));
    }

    [Fact]
    public async Task Filtered_by_repo_only_that_repos_archived_changes_count()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        await SeedTeamAsync(factory, clock);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>($"{MetricsEndpoints.MetricsPath}?repo=github.com/Emmz/panopticon", Ct);

        Assert.Equal(2, metrics.GetProperty("changes").GetInt32());
        Assert.Equal(12600L, metrics.GetProperty("medianArchiveSeconds").GetInt64());
        Assert.Equal(2 * 3600L, metrics.GetProperty("medianSectionSeconds").GetInt64());
        Assert.Equal(["beta", "alpha"], metrics.GetProperty("slowestSections").EnumerateArray().Select(s => s.GetProperty("change").GetString()));
    }

    [Fact]
    public async Task A_filter_matching_no_archived_change_has_no_figures()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        await SeedTeamAsync(factory, clock);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>($"{MetricsEndpoints.MetricsPath}?machine=nowhere", Ct);

        Assert.Equal(0, metrics.GetProperty("changes").GetInt32());
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("medianArchiveSeconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("medianSectionSeconds").ValueKind);
        Assert.Equal(MetricsReader.WeekCount, metrics.GetProperty("weeks").GetArrayLength());
        Assert.Empty(metrics.GetProperty("slowestSections").EnumerateArray());
    }

    [Fact]
    public async Task Unreadable_reports_are_left_out_of_the_section_times()
    {
        var (factory, clock) = await StartAsync();
        await using var _ = factory;
        await using (var mcp = await factory.CreateMcpClientAsync(cancellationToken: Ct))
        {
            await RunChangeAsync(mcp, clock, _panopticon, "alpha", sectionHours: 1, archiveHours: 2);
        }

        // Without the report that ticked it, the section finishes with the archived report, an hour later.
        await using (var db = factory.NewDb())
        {
            var ticked = await db.ReportEvents.OrderBy(e => e.Id).Skip(1).FirstAsync(Ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE report_events SET overall_done = NULL, overall_total = {ReportEvent.Unreadable}, current_section = NULL, sections = NULL WHERE id = {ticked.Id}",
                Ct);
        }

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var metrics = await http.GetFromJsonAsync<JsonElement>(MetricsEndpoints.MetricsPath, Ct);

        Assert.Equal(2 * 3600L, metrics.GetProperty("medianArchiveSeconds").GetInt64());
        Assert.Equal(2 * 3600L, Assert.Single(metrics.GetProperty("slowestSections").EnumerateArray()).GetProperty("durationSeconds").GetInt64());
    }

    [Fact]
    public async Task A_malformed_filter_is_a_400_naming_it()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        var response = await http.GetAsync($"{MetricsEndpoints.MetricsPath}?repo=panopticon", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("repo", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private async Task<(PanopticonFactory Factory, ManualClock Clock)> StartAsync()
    {
        var clock = ManualClock.StartingAt2026();
        var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct)) { Clock = clock };
        return (factory, clock);
    }

    /// <summary>
    /// From Sunday 8 Nov 09:00, one after another: alpha (section 1h, archive 2h) and beta (3h, 5h) in panopticon, gamma
    /// (2h, 10h) in meko, archived on Monday 9 Nov, and delta, still active.
    /// </summary>
    private static async Task<Dictionary<string, Guid>> SeedTeamAsync(PanopticonFactory factory, ManualClock clock)
    {
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: Ct);
        var ids = new Dictionary<string, Guid>
        {
            ["alpha"] = await RunChangeAsync(mcp, clock, _panopticon, "alpha", sectionHours: 1, archiveHours: 2),
            ["beta"] = await RunChangeAsync(mcp, clock, _panopticon, "beta", sectionHours: 3, archiveHours: 5),
            ["gamma"] = await RunChangeAsync(mcp, clock, _meko, "gamma", sectionHours: 2, archiveHours: 10),
        };
        await ReportAsync(mcp, _panopticon, "delta", Open, section: 1);
        clock.Advance(TimeSpan.FromHours(1));
        return ids;
    }

    /// <summary>Reports the change open, ticks its one section after a while, then archives it.</summary>
    private static async Task<Guid> RunChangeAsync(McpClient mcp, ManualClock clock, RepoRef repo, string change, int sectionHours, int archiveHours)
    {
        var id = await ReportAsync(mcp, repo, change, Open, section: 1);
        clock.Advance(TimeSpan.FromHours(sectionHours));
        await ReportAsync(mcp, repo, change, Ticked, section: 1);
        clock.Advance(TimeSpan.FromHours(archiveHours - sectionHours));
        await ReportAsync(mcp, repo, change, Ticked, section: null, archive: $"2026-11-08-{change}");
        return id;
    }

    private static async Task<Guid> ReportAsync(McpClient mcp, RepoRef repo, string change, string tasks, int? section, string? archive = null)
    {
        var report = new ReportProgressArguments(repo, change, new AgentRef("claude-code", "vm-1"), tasks, section, Archive: archive);
        var result = await mcp.CallToolAsync("report_progress", report.ToToolArguments(), cancellationToken: Ct);
        Assert.NotEqual(true, result.IsError);
        return Guid.Parse(result.StructuredContent!.Value.GetProperty("changeId").GetString()!);
    }

    private static (int, string?, long, bool) Section(JsonElement s) => (
        s.GetProperty("number").GetInt32(),
        s.GetProperty("title").GetString(),
        s.GetProperty("durationSeconds").GetInt64(),
        s.GetProperty("inProgress").GetBoolean());

    private static (DateTimeOffset, int, long) Week(JsonElement w) => (
        w.GetProperty("weekStart").GetDateTimeOffset(),
        w.GetProperty("changes").GetInt32(),
        w.GetProperty("medianArchiveSeconds").GetInt64());

    private static (Guid, string?, string?, int, string?, long) Slow(JsonElement s) => (
        s.GetProperty("changeId").GetGuid(),
        s.GetProperty("change").GetString(),
        s.GetProperty("repo").GetProperty("name").GetString(),
        s.GetProperty("number").GetInt32(),
        s.GetProperty("title").GetString(),
        s.GetProperty("durationSeconds").GetInt64());
}
