using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Changes;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// specs/change-filters against GET /api/history and GET /api/history/filters (filters-and-idle-cards D3, D4, D5):
// Filter by repo or owner, Filter by person, Filter by machine or agent type, Filters combine, History filtered by the
// server, Filter choices (History choices).
public class HistoryFilterTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset _start = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private const string Sam = "sam@example.com";

    private sealed record SeedAgent(string Client, string Machine, string? Member);

    private sealed record SeedChange(string Repo, string Name, DateTimeOffset? ArchivedAt, params SeedAgent[] Agents);

    // "emmz" is the factory's default member, "sam" a second one, null no member (a bootstrap report).
    private static readonly SeedChange[] _team =
    [
        new("github.com/emmz/panopticon", "team-auth", _start.AddMinutes(1), new SeedAgent("claude-code", "vm-1", "emmz"), new SeedAgent("claude-code", "vm-2", "sam")),
        new("github.com/emmz/panopticon", "walking-skeleton", _start.AddMinutes(2), new SeedAgent("cursor", "laptop", "sam")),
        new("github.com/emmz/other", "other-change", _start.AddMinutes(3), new SeedAgent("claude-code", "laptop", "emmz")),
        new("github.com/acme/site", "site-change", _start.AddMinutes(4), new SeedAgent("claude-code", "vm-1", "sam")),
        new("gitlab.com/team/web/app", "web-app", _start.AddMinutes(5), new SeedAgent("cursor", "vm-1", null)),
        new("gitlab.com/team/app", "team-app", _start.AddMinutes(6), new SeedAgent("cursor", "vm-1", null)),
        new("github.com/emmz/active-only", "still-going", null, new SeedAgent("claude-code", "vm-9", "emmz")),
    ];

    [Theory]
    // One repo, and case-insensitive matching.
    [InlineData("repo=github.com/emmz/panopticon", "team-auth,walking-skeleton")]
    [InlineData("repo=GitHub.com/Emmz/Panopticon", "team-auth,walking-skeleton")]
    // One owner across repos (not acme), and an owner with a subgroup, matched whole.
    [InlineData("owner=github.com/emmz", "team-auth,walking-skeleton,other-change")]
    [InlineData("owner=gitlab.com/team/web", "web-app")]
    [InlineData("owner=gitlab.com/team", "team-app")]
    [InlineData("repo=gitlab.com/team/web/app", "web-app")]
    // Changes a member worked on (the active one isn't history), and a shared change.
    [InlineData("member={emmz}", "team-auth,other-change")]
    [InlineData("member={sam}", "team-auth,walking-skeleton,site-change")]
    // One machine, one agent type.
    [InlineData("machine=vm-1", "team-auth,site-change,web-app,team-app")]
    [InlineData("machine=VM-1", "team-auth,site-change,web-app,team-app")]
    [InlineData("client=claude-code", "team-auth,other-change,site-change")]
    // Owner and person; machine and agent type.
    [InlineData("owner=github.com/emmz&member={emmz}", "team-auth,other-change")]
    [InlineData("owner=github.com/emmz&member={sam}", "team-auth,walking-skeleton")]
    [InlineData("machine=vm-2&client=claude-code", "team-auth")]
    // Matching nothing, and blank values as no filter.
    [InlineData("repo=github.com/emmz/nothing-here", "")]
    [InlineData("repo=&machine=", "team-auth,walking-skeleton,other-change,site-change,web-app,team-app")]
    public async Task History_filters_narrow_to_matching_archived_changes(string query, string expected)
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var members = await SeedAsync(factory, _team);

        var page = await http.GetFromJsonAsync<JsonElement>($"{ChangeEndpoints.HistoryPath}?{WithIds(query, members)}", Ct);

        Assert.Equal(
            expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(),
            page.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("change").GetString()!).Order());
    }

    [Theory]
    [InlineData("repo=github.com/emmz", "repo")]
    [InlineData("repo=github.com//panopticon", "repo")]
    [InlineData("repo=github.com/emmz/", "repo")]
    [InlineData("owner=github.com", "owner")]
    [InlineData("member=not-a-member-id", "member")]
    public async Task A_malformed_filter_is_a_bad_request_naming_it(string query, string parameter)
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var response = await http.GetAsync($"{ChangeEndpoints.HistoryPath}?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith(parameter, (await response.Content.ReadFromJsonAsync<string>(Ct))!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gitlab.com/team/web/app", "gitlab.com", "team/web", "app")]
    [InlineData(" GitHub.com/Emmz/Panopticon ", "github.com", "emmz", "panopticon")]
    public void A_repo_filter_splits_host_owner_and_name(string repo, string host, string owner, string name)
    {
        Assert.True(HistoryFilter.TryParse(repo, null, null, null, null, null, out var filter, out _));

        Assert.Equal(new RepoKey(host, owner, name), filter.Repo);
    }

    // "Filtered paging".
    [Fact]
    public async Task A_filtered_history_pages_fifty_at_a_time_within_the_filter()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        // 60 in panopticon and 40 elsewhere, interleaved in time so a page of the whole history would mix them.
        var seeded = Enumerable.Range(0, 100)
            .Select(i => new SeedChange(
                i % 5 < 3 ? "github.com/emmz/panopticon" : "github.com/emmz/other",
                $"change-{i:D3}",
                _start.AddMinutes(i),
                new SeedAgent("claude-code", "vm-1", "emmz")))
            .ToArray();
        await SeedAsync(factory, seeded);
        var inRepo = seeded.Where(c => c.Repo.EndsWith("/panopticon", StringComparison.Ordinal)).OrderByDescending(c => c.ArchivedAt).ToList();
        const string filter = "repo=github.com/emmz/panopticon";

        var first = await http.GetFromJsonAsync<JsonElement>($"{ChangeEndpoints.HistoryPath}?{filter}", Ct);
        var next = first.GetProperty("nextBefore").GetDateTimeOffset();
        var second = await http.GetFromJsonAsync<JsonElement>(
            $"{ChangeEndpoints.HistoryPath}?{filter}&before={Uri.EscapeDataString(next.ToString("O", CultureInfo.InvariantCulture))}",
            Ct);

        Assert.Equal(inRepo.Take(50).Select(c => c.Name), Names(first));
        Assert.Equal(inRepo.Skip(50).Select(c => c.Name), Names(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task A_filtered_page_never_ends_inside_a_run_of_equal_archive_times()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var agent = new SeedAgent("claude-code", "vm-1", "emmz");
        await SeedAsync(factory,
        [
            new("github.com/emmz/panopticon", "in-0", _start, agent),
            new("github.com/emmz/panopticon", "in-1", _start.AddMinutes(1), agent),
            new("github.com/emmz/other", "out-1", _start.AddMinutes(1), agent),
            new("github.com/emmz/panopticon", "in-2", _start.AddMinutes(1), agent),
            new("github.com/emmz/panopticon", "in-3", _start.AddMinutes(2), agent),
        ]);

        var seen = new List<string>();
        string? before = null;
        do
        {
            var url = $"{ChangeEndpoints.HistoryPath}?repo=github.com/emmz/panopticon&limit=2"
                + (before is null ? string.Empty : $"&before={Uri.EscapeDataString(before)}");
            var page = await http.GetFromJsonAsync<JsonElement>(url, Ct);
            seen.AddRange(Names(page));
            before = page.GetProperty("nextBefore").ValueKind == JsonValueKind.Null
                ? null
                : page.GetProperty("nextBefore").GetDateTimeOffset().ToString("O", CultureInfo.InvariantCulture);
        }
        while (before is not null);

        Assert.Equal(["in-0", "in-1", "in-2", "in-3"], seen.Order());
        Assert.Equal("in-3", seen[0]);
    }

    // "History choices", and "An agent with no member adds no person".
    [Fact]
    public async Task History_choices_are_the_values_among_archived_changes_only()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var members = await SeedAsync(factory, _team);

        var choices = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryFiltersPath, Ct);

        Assert.Equal(
            ["github.com/acme/site", "github.com/emmz/other", "github.com/emmz/panopticon", "gitlab.com/team/app", "gitlab.com/team/web/app"],
            choices.GetProperty("repos").EnumerateArray().Select(r =>
                $"{r.GetProperty("host").GetString()}/{r.GetProperty("owner").GetString()}/{r.GetProperty("name").GetString()}"));
        Assert.Equal(
            ["github.com/acme", "github.com/emmz", "gitlab.com/team", "gitlab.com/team/web"],
            choices.GetProperty("owners").EnumerateArray().Select(o => $"{o.GetProperty("host").GetString()}/{o.GetProperty("owner").GetString()}"));
        Assert.Equal(
            [(members["emmz"], "Emmz"), (members["sam"], "Sam")],
            choices.GetProperty("members").EnumerateArray().Select(m => (m.GetProperty("id").GetGuid(), m.GetProperty("displayName").GetString()!)));
        Assert.Equal(["laptop", "vm-1", "vm-2"], choices.GetProperty("machines").EnumerateArray().Select(m => m.GetString()!));
        Assert.Equal(["claude-code", "cursor"], choices.GetProperty("clients").EnumerateArray().Select(c => c.GetString()!));
    }

    // "Signed out", for the filtered history and its choices.
    [Theory]
    [InlineData(ChangeEndpoints.HistoryPath + "?repo=github.com/emmz/panopticon")]
    [InlineData(ChangeEndpoints.HistoryFiltersPath)]
    public async Task Filtered_history_and_its_choices_need_sign_in(string path)
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        await SeedAsync(factory, _team);
        using var http = factory.CreateBrowserClient();

        using var response = await http.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("team-auth", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static IEnumerable<string> Names(JsonElement page) =>
        page.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("change").GetString()!);

    private static string WithIds(string query, IReadOnlyDictionary<string, Guid> members) =>
        members.Aggregate(query, (q, m) => q.Replace($"{{{m.Key}}}", m.Value.ToString(), StringComparison.Ordinal));

    /// <summary>
    /// Writes the changes straight to the database, with the default member named Emmz and a second member, Sam.
    /// The server must have started (so the default member exists). Returns the members' ids by seed name.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, Guid>> SeedAsync(PanopticonFactory factory, IReadOnlyList<SeedChange> changes)
    {
        factory.CreateClient().Dispose();
        await using var db = factory.NewDb();
        var emmz = await db.Members.SingleAsync(m => m.EmailKey == PanopticonFactory.MemberEmail, Ct);
        emmz.DisplayName = "Emmz";
        var sam = new Member { Id = Guid.NewGuid(), Email = Sam, EmailKey = Sam, DisplayName = "Sam", CreatedAt = _start };
        db.Members.Add(sam);
        var members = new Dictionary<string, Guid> { ["emmz"] = emmz.Id, ["sam"] = sam.Id };

        var repos = new Dictionary<string, Repo>(StringComparer.Ordinal);
        var agents = new Dictionary<SeedAgent, Agent>();
        foreach (var seed in changes)
        {
            if (!repos.TryGetValue(seed.Repo, out var repo))
            {
                var segments = seed.Repo.Split('/');
                var (host, owner, name) = (segments[0], string.Join('/', segments[1..^1]), segments[^1]);
                repo = new Repo { Id = Guid.NewGuid(), Host = host, Owner = owner, Name = name, HostKey = host, OwnerKey = owner, NameKey = name };
                repos[seed.Repo] = repo;
                db.Repos.Add(repo);
            }

            var change = new Change
            {
                Id = Guid.NewGuid(),
                RepoId = repo.Id,
                Name = seed.Name,
                NameKey = seed.Name,
                Snapshot = new ProgressSnapshot(),
                LastReportedAt = seed.ArchivedAt ?? _start,
                ArchivedAt = seed.ArchivedAt,
                ArchiveFolder = seed.ArchivedAt is null ? null : $"2026-11-08-{seed.Name}",
            };
            db.Changes.Add(change);

            foreach (var seedAgent in seed.Agents)
            {
                if (!agents.TryGetValue(seedAgent, out var agent))
                {
                    agent = new Agent
                    {
                        Id = Guid.NewGuid(),
                        MemberId = seedAgent.Member is null ? null : members[seedAgent.Member],
                        ClientName = seedAgent.Client,
                        MachineName = seedAgent.Machine,
                        ClientNameKey = seedAgent.Client,
                        MachineNameKey = seedAgent.Machine,
                    };
                    agents[seedAgent] = agent;
                    db.Agents.Add(agent);
                }

                change.Agents.Add(new ChangeAgent { ChangeId = change.Id, AgentId = agent.Id, LastReportedAt = change.LastReportedAt });
            }
        }

        await db.SaveChangesAsync(Ct);
        return members;
    }
}
