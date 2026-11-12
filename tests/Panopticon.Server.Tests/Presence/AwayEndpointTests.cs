using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D3, D8; spec remote-answers, Away mode ("Away from the CLI", "Restart").
public class AwayEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient AgentClient(PanopticonFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");
        return http;
    }

    private static async Task<AwayState> PutAsync(HttpClient http, string path, bool away)
    {
        using var response = await http.SendApiAsync(HttpMethod.Put, path, new AwayState(away), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AwayState>(Ct))!;
    }

    [Fact]
    public async Task Away_from_the_CLI_reaches_the_members_open_dashboards_and_reads_back()
    {
        var clock = ManualClock.StartingAt2026();
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct, clock);
        using var agent = AgentClient(world.Factory, await world.Factory.MemberTokenAsync(Ct));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var emmzStream = await world.Emmz.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var emmzReader = new StreamReader(await emmzStream.Content.ReadAsStreamAsync(timeout.Token));

        Assert.False((await agent.GetFromJsonAsync<AwayState>(AwayEndpoints.AgentPath, Ct))!.Away);

        var on = await PutAsync(agent, AwayEndpoints.AgentPath, away: true);
        Assert.Equal(new AwayState(true, clock.GetUtcNow()), on);
        var pushed = System.Text.Json.JsonDocument.Parse(await SseReading.NextEventAsync(emmzReader, DashboardEndpoints.AwayChangedEvent, timeout.Token)).RootElement;
        Assert.True(pushed.GetProperty("away").GetBoolean());

        Assert.Equal(on, await agent.GetFromJsonAsync<AwayState>(AwayEndpoints.AgentPath, Ct));
        Assert.Equal(on, await world.Emmz.GetFromJsonAsync<AwayState>(AwayEndpoints.MePath, Ct));

        // On again keeps the time it was first turned on.
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(on, await PutAsync(agent, AwayEndpoints.AgentPath, away: true));

        // Off from the dashboard, which the CLI then sees.
        Assert.Equal(new AwayState(false), await PutAsync(world.Emmz, AwayEndpoints.MePath, away: false));
        Assert.False((await agent.GetFromJsonAsync<AwayState>(AwayEndpoints.AgentPath, Ct))!.Away);
    }

    [Fact]
    public async Task Away_changed_goes_only_to_that_members_streams()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var samStream = await world.Sam.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var samReader = new StreamReader(await samStream.Content.ReadAsStreamAsync(timeout.Token));

        await PutAsync(world.Emmz, AwayEndpoints.MePath, away: true);
        await PutAsync(world.Sam, AwayEndpoints.MePath, away: true);

        // Sam's first away-changed is his own: Emmz's never reached his stream.
        var lines = await SseReading.LinesUpToEventAsync(samReader, DashboardEndpoints.AwayChangedEvent, timeout.Token);
        Assert.Single(lines, l => l == $"event: {DashboardEndpoints.AwayChangedEvent}");
        Assert.True((await world.Sam.GetFromJsonAsync<AwayState>(AwayEndpoints.MePath, Ct))!.Away);
        Assert.True((await world.Emmz.GetFromJsonAsync<AwayState>(AwayEndpoints.MePath, Ct))!.Away);
        Assert.False((await world.Kit.GetFromJsonAsync<AwayState>(AwayEndpoints.MePath, Ct))!.Away);
    }

    [Fact]
    public async Task The_away_flag_survives_a_new_server_instance_on_the_same_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Ct);
        var bucket = await s3.CreateBucketAsync(Ct);
        string token;
        await using (var first = new PanopticonFactory(connectionString, bucket))
        {
            token = await first.MemberTokenAsync(Ct);
            using var agent = AgentClient(first, token);
            await PutAsync(agent, AwayEndpoints.AgentPath, away: true);
        }

        await using var second = new PanopticonFactory(connectionString, bucket);
        using var again = AgentClient(second, token);
        Assert.True((await again.GetFromJsonAsync<AwayState>(AwayEndpoints.AgentPath, Ct))!.Away);
    }

    [Fact]
    public async Task The_endpoints_need_their_own_kind_of_caller()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        using var anonymous = world.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");

        using (var response = await anonymous.GetAsync(AwayEndpoints.AgentPath, Ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var response = await anonymous.GetAsync(AwayEndpoints.MePath, Ct))
        {
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // A dashboard PUT without the cross-site header is refused (dashboard-sign-in D6).
        using var bare = new HttpRequestMessage(HttpMethod.Put, AwayEndpoints.MePath) { Content = JsonContent.Create(new AwayState(true)) };
        using (var response = await world.Emmz.SendAsync(bare, Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.False((await world.Emmz.GetFromJsonAsync<AwayState>(AwayEndpoints.MePath, Ct))!.Away);
    }

    [Fact]
    public async Task A_bootstrap_token_has_no_away_switch()
    {
        const string bootstrap = "dev-bootstrap-token";
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrap)
        {
            Environment = "Development",
        };
        using var agent = AgentClient(factory, bootstrap);

        using var response = await agent.GetAsync(AwayEndpoints.AgentPath, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
