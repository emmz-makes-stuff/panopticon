using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Metrics;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// projects D2; spec change-visibility: "Who sees a change", "Hidden changes don't exist", "Development bootstrap reports".
public class VisibilityTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string KitEmail = "kit@example.com";

    private const string Tasks = "## 1. Work\n- [x] 1.1\n- [ ] 1.2\n";

    // Emmz's personal tag on her PAN changes (tags D6): Kit shares those changes and must never see it.
    private const string EmmzOnlyTag = "emmz-only-tag";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_member_sees_their_Projects_changes_and_their_own_personal_ones_only()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        Assert.Equal(["pan-work"], await DashboardAsync(world.Kit));
        Assert.Equal(["acme-live", "pan-work", "scratch"], await DashboardAsync(world.Emmz));
    }

    [Fact]
    public async Task Metrics_count_only_the_changes_the_viewer_can_see()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        Assert.Equal(1, (await world.Kit.GetFromJsonAsync<JsonElement>(MetricsEndpoints.MetricsPath, Ct)).GetProperty("changes").GetInt32());
        Assert.Equal(2, (await world.Emmz.GetFromJsonAsync<JsonElement>(MetricsEndpoints.MetricsPath, Ct)).GetProperty("changes").GetInt32());
    }

    [Fact]
    public async Task Choices_dont_offer_a_value_seen_only_on_a_hidden_change()
    {
        await using var world = await World.CreateAsync(postgres, s3);

        Assert.DoesNotContain("acme-vm", await MachinesAsync(world.Kit));
        Assert.Contains("acme-vm", await MachinesAsync(world.Emmz));
    }

    // The sweep (D2): every change-reading endpoint, as an outsider, for another Project's changes, archived and active.
    // A new change-reading endpoint belongs here too.
    [Fact]
    public async Task To_an_outsider_another_Projects_changes_dont_exist_on_any_endpoint()
    {
        await using var world = await World.CreateAsync(postgres, s3);
        var archived = await world.ChangeIdAsync("acme-work");
        var active = await world.ChangeIdAsync("acme-live");

        foreach (var id in new[] { archived, active, Guid.NewGuid() })
        {
            foreach (var path in new[]
            {
                ChangeEndpoints.ChangePath(id),
                ChangeEndpoints.DocumentPath(id, "tasks.md"),
                ChangeEndpoints.DocumentPath(id, "proposal.md"),
                MetricsEndpoints.ChangeMetricsPath(id),
            })
            {
                using var response = await world.Kit.GetAsync(path, Ct);
                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path} answered {response.StatusCode}");
            }
        }

        var cards = await world.Kit.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct);
        Assert.DoesNotContain(cards.EnumerateArray(), c => c.GetProperty("changeId").GetGuid() == active);
        var history = await world.Kit.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, Ct);
        Assert.Equal(["pan-done"], history.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("change").GetString()));
        var filtered = await world.Kit.GetFromJsonAsync<JsonElement>($"{ChangeEndpoints.HistoryPath}?machine=acme-vm", Ct);
        Assert.Empty(filtered.GetProperty("changes").EnumerateArray());
        var filteredMetrics = await world.Kit.GetFromJsonAsync<JsonElement>($"{MetricsEndpoints.MetricsPath}?machine=acme-vm", Ct);
        Assert.Equal(0, filteredMetrics.GetProperty("changes").GetInt32());

        // The owner, by contrast, reads them all.
        using var owners = await world.Emmz.GetAsync(ChangeEndpoints.DocumentPath(archived, "proposal.md"), Ct);
        Assert.Equal(HttpStatusCode.OK, owners.StatusCode);

        // Emmz's personal tags on the PAN changes Kit shares never reach Kit (tags "Personal tags are private").
        var shared = new[] { await world.ChangeIdAsync("pan-work"), await world.ChangeIdAsync("pan-done") };
        string[] everyRead =
        [
            DashboardEndpoints.ChangesPath,
            ChangeEndpoints.HistoryPath,
            ChangeEndpoints.HistoryFiltersPath,
            MetricsEndpoints.MetricsPath,
            .. shared.SelectMany(id => new[] { ChangeEndpoints.ChangePath(id), MetricsEndpoints.ChangeMetricsPath(id) }),
        ];
        foreach (var path in everyRead)
        {
            var body = await world.Kit.GetStringAsync(path, Ct);
            Assert.DoesNotContain(EmmzOnlyTag, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(EmmzOnlyTag, await world.Emmz.GetStringAsync(DashboardEndpoints.ChangesPath, Ct), StringComparison.Ordinal);
        Assert.Contains(EmmzOnlyTag, await world.Emmz.GetStringAsync(ChangeEndpoints.HistoryPath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_Development_a_bootstrap_report_with_no_Project_is_seen_by_every_member()
    {
        const string bootstrap = "dev-bootstrap-token";
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrap)
        {
            Environment = "Development",
        };
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        await emmz.InviteToProjectAsync(pan, KitEmail, "viewer", Ct);
        using var kit = await factory.CreateSignedInClientAsync(KitEmail, Ct);
        await using (var client = await factory.CreateMcpClientAsync(bootstrap, Ct))
        {
            await ReportAsync(client, Report("walking-skeleton"));
        }

        Assert.Equal(["walking-skeleton"], await DashboardAsync(emmz));
        Assert.Equal(["walking-skeleton"], await DashboardAsync(kit));
    }

    private static ReportProgressArguments Report(
        string change,
        string machine = "vm-1",
        Guid? project = null,
        bool archived = false,
        IReadOnlyList<string>? personalTags = null) =>
        new(new RepoRef("github.com", "emmz", "panopticon"), change, new AgentRef("claude-code", machine), Tasks)
        {
            Project = project?.ToString(),
            PersonalTags = personalTags,
            Documents = [new ChangeDocument("tasks.md", Tasks), new ChangeDocument("proposal.md", $"# {change}\n")],
            Archive = archived ? $"2026-11-08-{change}" : null,
        };

    private static async Task ReportAsync(ModelContextProtocol.Client.McpClient client, ReportProgressArguments report)
    {
        var result = await client.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct);
        Assert.True(result.IsError is not true, string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text)));
    }

    private static async Task<List<string>> DashboardAsync(HttpClient browser) =>
        [.. (await browser.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct)).EnumerateArray()
            .Select(c => c.GetProperty("change").GetString()!)
            .Order(StringComparer.Ordinal)];

    private static async Task<List<string>> MachinesAsync(HttpClient browser) =>
        [.. (await browser.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryFiltersPath, Ct)).GetProperty("machines").EnumerateArray()
            .Select(m => m.GetString()!)];

    /// <summary>
    /// Emmz owns <c>PAN</c> and <c>ACME</c>; Kit is a viewer of <c>PAN</c> only. Emmz has reported, in <c>PAN</c>,
    /// <c>pan-work</c> (active) and <c>pan-done</c> (archived); in <c>ACME</c>, from machine <c>acme-vm</c>,
    /// <c>acme-live</c> (active) and <c>acme-work</c> (archived); and her own <c>scratch</c>. Her agent tagged both
    /// <c>PAN</c> changes <see cref="EmmzOnlyTag"/>.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        public required PanopticonFactory Factory { get; init; }

        public required HttpClient Emmz { get; init; }

        public required HttpClient Kit { get; init; }

        public static async Task<World> CreateAsync(PostgresFixture postgres, S3Fixture s3)
        {
            var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
            var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
            var pan = await emmz.CreateProjectAsync("PAN", Ct);
            var acme = await emmz.CreateProjectAsync("ACME", Ct);
            await emmz.InviteToProjectAsync(pan, KitEmail, "viewer", Ct);
            var kit = await factory.CreateSignedInClientAsync(KitEmail, Ct);

            await using (var client = await factory.CreateMcpClientAsync(Ct))
            {
                await ReportAsync(client, Report("pan-work", project: pan, personalTags: [EmmzOnlyTag]));
                await ReportAsync(client, Report("pan-done", project: pan, personalTags: [EmmzOnlyTag]));
                await ReportAsync(client, Report("pan-done", project: pan, archived: true));
                await ReportAsync(client, Report("acme-live", "acme-vm", acme));
                await ReportAsync(client, Report("acme-work", "acme-vm", acme));
                await ReportAsync(client, Report("acme-work", "acme-vm", acme, archived: true));
                await ReportAsync(client, Report("scratch"));
            }

            return new World { Factory = factory, Emmz = emmz, Kit = kit };
        }

        public async Task<Guid> ChangeIdAsync(string name)
        {
            await using var db = Factory.NewDb();
            return await db.Changes.Where(c => c.Name == name).Select(c => c.Id).SingleAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            Emmz.Dispose();
            Kit.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
