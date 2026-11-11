using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Metrics;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D3, D4; spec agent-presence: Session identity, Session states, Presence is not history, Who sees a
// session; live-dashboard: Quiet cards ("Running gates for an hour").
public class AgentSessionEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string OutsiderEmail = "out@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RepoRef _repo = new("github.com", "emmz", "panopticon");

    private static SessionEventRequest Event(
        string change,
        Guid? project,
        string session = "s-1",
        SessionState state = SessionState.Working,
        string? activity = "Run make gates") =>
        new(_repo, change, "vm-1", session, state, activity, project?.ToString());

    private static async Task<HttpClient> AgentClientAsync(TagWorld world, string? token = null)
    {
        var http = world.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? await world.Factory.MemberTokenAsync(Ct));
        http.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");
        return http;
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient agent, object body)
    {
        using var response = await agent.PostAsJsonAsync(AgentSessionEndpoints.EventsPath, body, Ct);
        return response.StatusCode;
    }

    private static async Task<JsonElement> CardAsync(HttpClient browser, string change)
    {
        var cards = await browser.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct);
        return cards.EnumerateArray().Single(c => c.GetProperty("change").GetString() == change);
    }

    [Fact]
    public async Task Two_sessions_on_one_change_show_on_its_card_each_with_its_activity_and_member()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        using var agent = await AgentClientAsync(world);

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("team-auth", world.Pan, "s-1", activity: "Run make gates")));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("Team-Auth", world.Pan, "s-2", activity: "Editing design.md")));

        var sessions = (await CardAsync(world.Emmz, "team-auth")).GetProperty("sessions").EnumerateArray().ToList();
        Assert.Equal(["Run make gates", "Editing design.md"], sessions.Select(s => s.GetProperty("activity").GetString()));
        Assert.All(sessions, s =>
        {
            Assert.Equal("working", s.GetProperty("state").GetString());
            Assert.Equal("vm-1", s.GetProperty("machine").GetString());
            Assert.Equal("member", s.GetProperty("memberName").GetString());
        });

        // Claude Code's own session id stays on the server.
        Assert.DoesNotContain("\"s-1\"", (await CardAsync(world.Emmz, "team-auth")).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_event_for_a_change_with_no_card_is_accepted_and_shows_nowhere()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        using var agent = await AgentClientAsync(world);

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("tags", world.Pan)));

        // Named without its Project, the change is looked for in Emmz's personal scope, where there's none either.
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("team-auth", project: null, "s-2")));

        // A Project that isn't hers, or isn't an id, is a change with no card, not an error.
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("team-auth", Guid.NewGuid(), "s-3")));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("team-auth", project: null, "s-4") with { Project = "PAN" }));

        Assert.Equal(0, (await CardAsync(world.Emmz, "team-auth")).GetProperty("sessions").GetArrayLength());
    }

    [Fact]
    public async Task Session_states_follow_the_events_and_session_end_removes_it()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        using var agent = await AgentClientAsync(world);

        await PostAsync(agent, Event("team-auth", world.Pan, state: SessionState.WaitingOnUser, activity: null));
        var session = Assert.Single((await CardAsync(world.Emmz, "team-auth")).GetProperty("sessions").EnumerateArray());
        Assert.Equal("waiting-on-user", session.GetProperty("state").GetString());
        Assert.False(session.TryGetProperty("activity", out var activity) && activity.ValueKind != JsonValueKind.Null);

        await PostAsync(agent, Event("team-auth", world.Pan, state: SessionState.Working, activity: "Editing tasks.md"));
        session = Assert.Single((await CardAsync(world.Emmz, "team-auth")).GetProperty("sessions").EnumerateArray());
        Assert.Equal(("working", "Editing tasks.md"), (session.GetProperty("state").GetString(), session.GetProperty("activity").GetString()));

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("team-auth", world.Pan, state: SessionState.Ended, activity: null)));
        Assert.Equal(0, (await CardAsync(world.Emmz, "team-auth")).GetProperty("sessions").GetArrayLength());
    }

    [Fact]
    public async Task Malformed_events_are_refused()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        using var agent = await AgentClientAsync(world);

        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(agent, Event("c", null, activity: new string('a', 121))));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(agent, Event("c", null, activity: "two\nlines")));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(agent, Event(" ", null)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(agent, Event("c", null, session: "")));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(agent, Event("c", null) with { Machine = new string('m', 101) }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(agent, new { repo = _repo, change = "c", machine = "m", sessionId = "s", state = "sleeping" }));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(agent, Event("c", null, activity: new string('a', 120))));
    }

    [Fact]
    public async Task Without_a_token_the_endpoint_refuses()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        using var anonymous = world.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(anonymous, Event("c", null)));

        // A dashboard session cookie isn't a reporter's token.
        using var response = await world.Emmz.PostApiAsync(AgentSessionEndpoints.EventsPath, Event("c", null), Ct);
        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    // live-dashboard "Running gates for an hour": the card's last activity moves on; its order stays by last report.
    [Fact]
    public async Task A_session_event_moves_last_active_but_not_the_order()
    {
        var clock = ManualClock.StartingAt2026();
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct, clock);
        await world.ReportChangeAsync(TagWorld.Report("older", world.Pan), Ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        await world.ReportChangeAsync(TagWorld.Report("newer", world.Pan), Ct);
        clock.Advance(TimeSpan.FromHours(1));
        using var agent = await AgentClientAsync(world);

        await PostAsync(agent, Event("older", world.Pan));

        var cards = (await world.Emmz.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct)).EnumerateArray().ToList();
        Assert.Equal(["newer", "older"], cards.Select(c => c.GetProperty("change").GetString()));
        var older = cards[1];
        Assert.Equal(clock.GetUtcNow(), older.GetProperty("lastActiveAt").GetDateTimeOffset());
        Assert.Equal(clock.GetUtcNow() - TimeSpan.FromHours(1) - TimeSpan.FromMinutes(1), older.GetProperty("lastReportedAt").GetDateTimeOffset());
        Assert.Equal(cards[0].GetProperty("lastReportedAt").GetDateTimeOffset(), cards[0].GetProperty("lastActiveAt").GetDateTimeOffset());
    }

    // spec agent-presence "Metrics unaffected": session events never become report events.
    [Fact]
    public async Task Four_hundred_session_events_leave_history_and_metrics_unchanged()
    {
        // A manual clock: an active change's duration grows with time, which isn't what this checks.
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct, ManualClock.StartingAt2026());
        var changeId = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        using var agent = await AgentClientAsync(world);
        var metricsBefore = await world.Emmz.GetStringAsync(MetricsEndpoints.ChangeMetricsPath(changeId), Ct);

        for (var i = 0; i < 400; i++)
        {
            await PostAsync(agent, Event("team-auth", world.Pan, $"s-{i % 3}", activity: $"Step {i}"));
        }

        Assert.Equal(metricsBefore, await world.Emmz.GetStringAsync(MetricsEndpoints.ChangeMetricsPath(changeId), Ct));
        await using var db = world.Factory.NewDb();
        Assert.Equal(1, await db.ReportEvents.CountAsync(e => e.ChangeId == changeId, Ct));
        Assert.Equal(3, (await CardAsync(world.Emmz, "team-auth")).GetProperty("sessions").GetArrayLength());
    }

    // spec agent-presence "Outsider": neither reads nor the live stream mention a session to someone outside PAN.
    [Fact]
    public async Task A_member_outside_the_Project_never_sees_its_sessions()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var acme = await world.Emmz.CreateProjectAsync("ACME", Ct);
        await world.Emmz.InviteToProjectAsync(acme, OutsiderEmail, "viewer", Ct);
        using var outsider = await world.Factory.CreateSignedInClientAsync(OutsiderEmail, Ct);
        var changeId = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        await world.ReportChangeAsync(TagWorld.Report("acme-work", acme), Ct);
        using var agent = await AgentClientAsync(world);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var outsiderStream = await outsider.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var outsiderReader = new StreamReader(await outsiderStream.Content.ReadAsStreamAsync(timeout.Token));
        using var samStream = await world.Sam.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var samReader = new StreamReader(await samStream.Content.ReadAsStreamAsync(timeout.Token));

        await PostAsync(agent, Event("team-auth", world.Pan, "s-pan", activity: "Secret PAN work"));
        await PostAsync(agent, Event("team-auth", world.Pan, "s-pan", state: SessionState.Ended, activity: null));
        await PostAsync(agent, Event("acme-work", acme, "s-acme", activity: "Visible ACME work"));

        // Sam, in PAN, sees the PAN session come and go.
        var updated = JsonDocument.Parse(await SseReading.NextEventAsync(samReader, DashboardEndpoints.SessionUpdatedEvent, timeout.Token)).RootElement;
        Assert.Equal((changeId, "Secret PAN work"), (updated.GetProperty("changeId").GetGuid(), updated.GetProperty("session").GetProperty("activity").GetString()));
        var ended = JsonDocument.Parse(await SseReading.NextEventAsync(samReader, DashboardEndpoints.SessionEndedEvent, timeout.Token)).RootElement;
        Assert.Equal(updated.GetProperty("session").GetProperty("id").GetGuid(), ended.GetProperty("sessionId").GetGuid());

        // The outsider's first session event is ACME's: nothing of PAN's came before it.
        var lines = await SseReading.LinesUpToEventAsync(outsiderReader, DashboardEndpoints.SessionUpdatedEvent, timeout.Token);
        Assert.Contains("Visible ACME work", lines[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("PAN work", StringComparison.Ordinal) || l.Contains(changeId.ToString(), StringComparison.Ordinal));

        var cards = await outsider.GetStringAsync(DashboardEndpoints.ChangesPath, Ct);
        Assert.DoesNotContain("PAN work", cards, StringComparison.Ordinal);
        Assert.Contains("Visible ACME work", cards, StringComparison.Ordinal);
        using var page = await outsider.GetAsync($"{DashboardEndpoints.ChangesPath}/{changeId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }

    [Fact]
    public async Task The_change_page_shows_its_sessions()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var changeId = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        using var agent = await AgentClientAsync(world);
        await PostAsync(agent, Event("team-auth", world.Pan));

        var page = await world.Sam.GetFromJsonAsync<JsonElement>($"{DashboardEndpoints.ChangesPath}/{changeId}", Ct);

        Assert.Equal("Run make gates", Assert.Single(page.GetProperty("card").GetProperty("sessions").EnumerateArray()).GetProperty("activity").GetString());
    }

    [Fact]
    public async Task The_store_is_the_same_one_the_endpoint_records_into()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        using var agent = await AgentClientAsync(world);
        await PostAsync(agent, Event("no-card-yet", null));

        Assert.Equal(1, world.Factory.Services.GetRequiredService<PresenceStore>().Count);
    }
}
