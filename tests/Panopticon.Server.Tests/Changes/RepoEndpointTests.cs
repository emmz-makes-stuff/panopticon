using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Contracts;
using Panopticon.Server.Changes;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// spec repos (Repos list, Repos order, Archived changes on the Repos page), design D1. Visibility is in
// VisibilityTests' sweep, and retired copies in RetiredCopyTests.
public class RepoEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static readonly DateTimeOffset _day = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_repo_lists_its_active_changes_newest_report_first_then_its_archived_ones()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await SeedAsync(factory, ("github.com", "emmz-makes-stuff", "panopticon"),
            ("older-work", _day, null),
            ("repos-page", _day.AddHours(2), null),
            ("visit-requests", _day.AddDays(-1), _day.AddHours(1)),
            ("logo", _day.AddDays(-3), _day.AddDays(-2)));

        var repo = Assert.Single(await ReposAsync(browser));

        var identity = repo.GetProperty("repo");
        Assert.Equal(("github.com", "emmz-makes-stuff", "panopticon"),
            (identity.GetProperty("host").GetString(), identity.GetProperty("owner").GetString(), identity.GetProperty("name").GetString()));
        Assert.Equal(["repos-page", "older-work"], Names(repo, "active"));
        Assert.Equal(["visit-requests", "logo"], Names(repo, "archived"));
        Assert.Equal(_day.AddHours(2), repo.GetProperty("latestActivityAt").GetDateTimeOffset());
        Assert.Equal(0, repo.GetProperty("moreArchived").GetInt32());

        var active = repo.GetProperty("active")[0];
        Assert.Equal(_day.AddHours(2), active.GetProperty("lastReportedAt").GetDateTimeOffset());
        Assert.False(active.GetProperty("personal").GetBoolean());
        Assert.Equal(JsonValueKind.Null, active.GetProperty("project").ValueKind);
        Assert.Equal(_day.AddHours(1), repo.GetProperty("archived")[0].GetProperty("archivedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Repos_are_ordered_by_their_latest_report_or_archive_then_by_name()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await SeedAsync(factory, ("github.com", "emmz", "a"), ("only", _day, null));
        await SeedAsync(factory, ("github.com", "emmz", "b"), ("done", _day.AddDays(-5), _day.AddDays(2)));
        await SeedAsync(factory, ("dev.azure.com", "cpointrepos/SMART-NOVA", "NOVA-INFRA"), ("same-time", _day, null));

        var names = (await ReposAsync(browser)).Select(r => r.GetProperty("repo").GetProperty("name").GetString()).ToList();

        // b's archive (day +2) is the newest; a and NOVA-INFRA tie on _day, and dev.azure.com sorts before github.com.
        Assert.Equal(["b", "NOVA-INFRA", "a"], names);
    }

    [Fact]
    public async Task A_repo_shows_its_five_newest_archived_changes_and_counts_the_rest()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await SeedAsync(factory, ("github.com", "emmz", "panopticon"),
            [.. Enumerable.Range(0, 8).Select(i => ($"done-{i}", _day.AddDays(i), (DateTimeOffset?)_day.AddDays(i)))]);

        var repo = Assert.Single(await ReposAsync(browser));

        Assert.Equal(["done-7", "done-6", "done-5", "done-4", "done-3"], Names(repo, "archived"));
        Assert.Equal(3, repo.GetProperty("moreArchived").GetInt32());
        Assert.Empty(repo.GetProperty("active").EnumerateArray());
    }

    [Fact]
    public async Task A_repo_whose_changes_the_viewer_cant_see_isnt_listed()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        // Sam can sign in once invited to a Project, but shares none of its changes with Emmz's personal one.
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        await emmz.InviteToProjectAsync(pan, "sam@example.com", "viewer", Ct);
        using var sam = await factory.CreateSignedInClientAsync("sam@example.com", Ct);
        await using (var mcp = await factory.CreateMcpClientAsync(Ct))
        {
            // Emmz's personal change: hers alone (spec change-visibility).
            var report = new ReportProgressArguments(
                new RepoRef("dev.azure.com", "cpointrepos/SMART-NOVA", "NOVA-INFRA"),
                "infra-work",
                new AgentRef("claude-code", "vm-1"),
                "## 1. Work\n- [ ] 1.1\n");
            var result = await mcp.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct);
            Assert.NotEqual(true, result.IsError);
        }

        Assert.Equal(["NOVA-INFRA"], (await ReposAsync(emmz)).Select(r => r.GetProperty("repo").GetProperty("name").GetString()));
        Assert.Empty(await ReposAsync(sam));
        Assert.DoesNotContain("NOVA-INFRA", await sam.GetStringAsync(ChangeEndpoints.ReposPath, Ct), StringComparison.Ordinal);
    }

    // spec repos, "Active change progress on the Repos page"; design D6.
    [Fact]
    public async Task An_active_change_carries_its_whole_change_progress_and_an_archived_one_none()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        const string sevenOfEight = "## 1. One\n- [x] 1.1\n- [x] 1.2\n- [x] 1.3\n- [x] 1.4\n## 2. Two\n- [x] 2.1\n- [x] 2.2\n- [x] 2.3\n- [ ] 2.4\n";
        await using (var mcp = await factory.CreateMcpClientAsync(Ct))
        {
            foreach (var (change, archive) in new[] { ("repos-page", (string?)null), ("logo", null), ("logo", "2026-11-12-logo") })
            {
                var report = new ReportProgressArguments(
                    new RepoRef("github.com", "emmz-makes-stuff", "panopticon"), change, new AgentRef("claude-code", "vm-1"), sevenOfEight)
                {
                    Archive = archive,
                };
                var result = await mcp.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct);
                Assert.NotEqual(true, result.IsError);
            }
        }

        var repo = Assert.Single(await ReposAsync(emmz));

        var overall = Assert.Single(repo.GetProperty("active").EnumerateArray()).GetProperty("overall");
        Assert.Equal((7, 8), (overall.GetProperty("done").GetInt32(), overall.GetProperty("total").GetInt32()));
        Assert.False(Assert.Single(repo.GetProperty("archived").EnumerateArray()).TryGetProperty("overall", out _));
    }

    [Fact]
    public async Task With_no_change_the_list_is_empty()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        Assert.Empty(await ReposAsync(browser));
    }

    [Fact]
    public async Task Not_signed_in_gets_a_401_and_no_repo()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        await SeedAsync(factory, ("github.com", "emmz", "secret-repo"), ("work", _day, null));
        using var http = factory.CreateBrowserClient();

        using var response = await http.GetAsync(ChangeEndpoints.ReposPath, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("secret-repo", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static async Task<List<JsonElement>> ReposAsync(HttpClient browser) =>
        [.. (await browser.GetFromJsonAsync<JsonElement>(ChangeEndpoints.ReposPath, Ct)).GetProperty("repos").EnumerateArray()];

    private static List<string?> Names(JsonElement repo, string list) =>
        [.. repo.GetProperty(list).EnumerateArray().Select(c => c.GetProperty("change").GetString())];

    // A repo and its changes, with no Project and no owner (seen by every member), each last reported and maybe archived at the given times.
    private static async Task SeedAsync(
        PanopticonFactory factory,
        (string Host, string Owner, string Name) identity,
        params (string Name, DateTimeOffset ReportedAt, DateTimeOffset? ArchivedAt)[] changes)
    {
        await using var db = factory.NewDb();
        var repo = new Repo
        {
            Id = Guid.NewGuid(),
            Host = identity.Host,
            Owner = identity.Owner,
            Name = identity.Name,
            HostKey = identity.Host.ToLowerInvariant(),
            OwnerKey = identity.Owner.ToLowerInvariant(),
            NameKey = identity.Name.ToLowerInvariant(),
        };
        db.Repos.Add(repo);
        foreach (var change in changes)
        {
            db.Changes.Add(new Change
            {
                Id = Guid.NewGuid(),
                RepoId = repo.Id,
                Name = change.Name,
                NameKey = change.Name,
                Snapshot = new ProgressSnapshot(),
                LastReportedAt = change.ReportedAt,
                ArchivedAt = change.ArchivedAt,
                ArchiveFolder = change.ArchivedAt is { } at ? $"{at:yyyy-MM-dd}-{change.Name}" : null,
            });
        }

        await db.SaveChangesAsync(Ct);
    }
}
