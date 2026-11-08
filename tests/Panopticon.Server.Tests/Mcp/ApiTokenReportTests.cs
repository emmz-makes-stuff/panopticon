using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Mcp;

// specs/progress-reporting: "Bearer token required" (member tokens), "Agents on a change" (member identity);
// specs/reporter-credentials: "Tokens page" ("Last used", and the /mcp half of "List and revoke");
// specs/live-dashboard and team-members: the card's member (API side), "Reports remain" (design D4, D16).
public class ApiTokenReportTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Sam = "sam@example.com";

    private static ReportProgressArguments Report(string client = "claude-code", string machine = "vm-1") => new(
        new RepoRef("github.com", "emmz", "panopticon"),
        "team-auth",
        new AgentRef(client, machine),
        "- [x] 1.1\n- [ ] 1.2\n");

    /// <summary>A server outside Development, with no bootstrap token: reports need a member's API token.</summary>
    private async Task<PanopticonFactory> FactoryAsync(ManualClock? clock = null, string environment = "Testing", string? bootstrapToken = null) =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken)
        {
            Environment = environment,
            Clock = clock,
        };

    private static async Task ReportAsync(PanopticonFactory factory, string token, ReportProgressArguments? report = null)
    {
        await using var client = await factory.CreateMcpClientAsync(token, Ct);
        var result = await client.CallToolAsync(ContractsAssembly.ReportProgressToolName, (report ?? Report()).ToToolArguments(), cancellationToken: Ct);
        Assert.True(
            result.IsError is not true,
            string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text)));
    }

    /// <summary>A raw tool call, to see the HTTP status a refused token gets.</summary>
    private static async Task<HttpResponseMessage> PostToolCallAsync(PanopticonFactory factory, string token)
    {
        using var http = factory.CreateClient();
        var body = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"report_progress","arguments":{
            "repo":{"host":"github.com","owner":"emmz","name":"panopticon"},"change":"team-auth",
            "agent":{"client":"claude-code","machine":"vm-1"},"tasksMarkdown":"- [x] a"}}}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, ServerSetup.McpPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> SingleCardAsync(HttpClient browser)
    {
        var cards = await browser.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct);
        return Assert.Single(cards.EnumerateArray().ToList());
    }

    private static List<(string Client, string Machine, string? Member)> Agents(JsonElement card) =>
        [.. card.GetProperty("agents").EnumerateArray().Select(a => (
            a.GetProperty("client").GetString()!,
            a.GetProperty("machine").GetString()!,
            a.TryGetProperty("member", out var member) ? member.GetString() : null))];

    private static async Task AssertNothingRecordedAsync(PanopticonFactory factory)
    {
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.ReportEvents.CountAsync(Ct));
    }

    [Fact]
    public async Task A_members_token_is_accepted_and_the_agent_is_recorded_with_that_member()
    {
        await using var factory = await FactoryAsync();
        var token = await factory.CreateApiTokenAsync(cancellationToken: Ct);

        await ReportAsync(factory, token);

        await using var db = factory.NewDb();
        var agent = await db.Agents.Include(a => a.Member).SingleAsync(Ct);
        Assert.Equal(PanopticonFactory.MemberEmail, agent.Member!.EmailKey);
    }

    // "Revoked token" (revoked by its member), and the /mcp half of "List and revoke".
    [Fact]
    public async Task A_revoked_token_is_refused_and_records_nothing()
    {
        await using var factory = await FactoryAsync();
        var token = await factory.CreateApiTokenAsync(cancellationToken: Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var listed = Assert.Single((await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!);
        (await browser.SendApiAsync(HttpMethod.Delete, $"{TokenEndpoints.TokensPath}/{listed.Id}", null, Ct)).Dispose();

        using var response = await PostToolCallAsync(factory, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await AssertNothingRecordedAsync(factory);
    }

    // "Revoked token" (a removed member's), and "Remove a colleague": Sam's reporters are refused.
    [Fact]
    public async Task A_removed_members_token_is_refused_and_records_nothing()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = (await (await browser.PostApiAsync(MemberEndpoints.MembersPath, new { email = Sam }, Ct)).Content.ReadFromJsonAsync<MemberView>(Ct))!;
        var token = await factory.CreateApiTokenAsync(Sam, cancellationToken: Ct);
        (await browser.SendApiAsync(HttpMethod.Delete, $"{MemberEndpoints.MembersPath}/{sam.Id}", null, Ct)).Dispose();

        using var response = await PostToolCallAsync(factory, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecordedAsync(factory);
    }

    [Fact]
    public async Task The_bootstrap_token_is_refused_outside_development_even_when_it_matches_a_setting()
    {
        // Testing with no bootstrap token configured: a made-up token in the bootstrap's place is just a wrong token.
        await using var factory = await FactoryAsync();

        using var response = await PostToolCallAsync(factory, PanopticonFactory.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecordedAsync(factory);
    }

    // "Bootstrap token in Development", and "Agent without a member" (API side).
    [Fact]
    public async Task The_bootstrap_token_in_development_records_an_agent_with_no_member()
    {
        await using var factory = await FactoryAsync(environment: "Development", bootstrapToken: PanopticonFactory.Token);

        await ReportAsync(factory, PanopticonFactory.Token);

        await using (var db = factory.NewDb())
        {
            Assert.Null((await db.Agents.SingleAsync(Ct)).MemberId);
        }

        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        Assert.Equal([("claude-code", "vm-1", (string?)null)], Agents(await SingleCardAsync(browser)));
    }

    // "Two members on one machine", and "Card shows identity and agents" (API side).
    [Fact]
    public async Task Two_members_on_one_machine_are_two_agents_each_named_on_the_card()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await browser.PostApiAsync(MemberEndpoints.MembersPath, new { email = Sam, displayName = "Sam" }, Ct)).Dispose();
        (await browser.SendApiAsync(HttpMethod.Patch, MemberEndpoints.MePath, new { displayName = "Emmz" }, Ct)).Dispose();
        var emmzToken = await factory.CreateApiTokenAsync(cancellationToken: Ct);
        var samToken = await factory.CreateApiTokenAsync(Sam, cancellationToken: Ct);

        await ReportAsync(factory, emmzToken);
        await ReportAsync(factory, samToken);
        await ReportAsync(factory, emmzToken);

        var agents = Agents(await SingleCardAsync(browser));
        Assert.Equal(2, agents.Count);
        Assert.Equal(["Emmz", "Sam"], agents.Select(a => a.Member).Order());
        Assert.All(agents, a => Assert.Equal(("claude-code", "vm-1"), (a.Client, a.Machine)));
    }

    // "Rename yourself": the card shows the new name on the next load (design D16: no rename broadcast).
    [Fact]
    public async Task A_rename_shows_on_the_card_at_the_next_load()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await ReportAsync(factory, await factory.CreateApiTokenAsync(cancellationToken: Ct));
        Assert.Equal("member", Assert.Single(Agents(await SingleCardAsync(browser))).Member);

        (await browser.SendApiAsync(HttpMethod.Patch, MemberEndpoints.MePath, new { displayName = "Emmz" }, Ct)).Dispose();

        Assert.Equal("Emmz", Assert.Single(Agents(await SingleCardAsync(browser))).Member);
    }

    // "Reports remain".
    [Fact]
    public async Task A_removed_members_agent_stays_on_the_card_with_their_name()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = (await (await browser.PostApiAsync(MemberEndpoints.MembersPath, new { email = Sam, displayName = "Sam" }, Ct)).Content.ReadFromJsonAsync<MemberView>(Ct))!;
        await ReportAsync(factory, await factory.CreateApiTokenAsync(Sam, cancellationToken: Ct));

        (await browser.SendApiAsync(HttpMethod.Delete, $"{MemberEndpoints.MembersPath}/{sam.Id}", null, Ct)).Dispose();

        Assert.Equal([("claude-code", "vm-1", (string?)"Sam")], Agents(await SingleCardAsync(browser)));
    }

    // "Last used": moves to about the report's time, written at most once a minute.
    [Fact]
    public async Task Last_used_moves_with_reports_at_most_once_a_minute()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        var token = await factory.CreateApiTokenAsync(cancellationToken: Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        async Task<DateTimeOffset?> LastUsedAsync() =>
            Assert.Single((await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!).LastUsedAt;

        Assert.Null(await LastUsedAsync());
        var first = clock.GetUtcNow();
        await ReportAsync(factory, token);
        Assert.Equal(first, await LastUsedAsync());

        clock.Advance(TimeSpan.FromSeconds(30));
        await ReportAsync(factory, token);
        Assert.Equal(first, await LastUsedAsync());

        clock.Advance(TimeSpan.FromSeconds(30));
        await ReportAsync(factory, token);
        Assert.Equal(clock.GetUtcNow(), await LastUsedAsync());
    }
}
