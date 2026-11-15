using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D3, D6; spec remote-answers: A question while not away, A prompt while away, Releasing a prompt,
// Permission input stays local unless held, Prompt limits; agent-presence: A held prompt keeps its session.
public class AgentPromptEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RepoRef _repo = new("github.com", "emmz", "panopticon");

    private static readonly PromptQuestion _store = new(
        "Which store?",
        "Store",
        [new PromptOption("Memory"), new PromptOption("Postgres", Preview: "```sql\nselect 1;\n```")]);

    private sealed class World(TagWorld tags, ManualClock clock, HttpClient agent) : IAsyncDisposable
    {
        public TagWorld Tags { get; } = tags;
        public ManualClock Clock { get; } = clock;
        public HttpClient Agent { get; } = agent;
        public PresenceStore Store => Tags.Factory.Services.GetRequiredService<PresenceStore>();

        public async ValueTask DisposeAsync()
        {
            Agent.Dispose();
            await Tags.DisposeAsync();
        }
    }

    // A long grace stands for a hook that keeps calling wait while the fake clock jumps hours ahead.
    private const string HookAlwaysWaiting = "1.00:00:00";

    private async Task<World> WorldAsync(bool away, string waitTimeout = "00:00:01", string waitGrace = "00:01:30")
    {
        var clock = ManualClock.StartingAt2026();
        var tags = await TagWorld.CreateAsync(postgres, s3, Ct, clock, new Dictionary<string, string>
        {
            [$"{PromptOptions.Section}:WaitTimeout"] = waitTimeout,
            [$"{PromptOptions.Section}:WaitGrace"] = waitGrace,
        });
        await tags.ReportChangeAsync(TagWorld.Report("team-auth", tags.Pan), Ct);
        var agent = tags.Factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await tags.Factory.MemberTokenAsync(Ct));
        agent.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");
        if (away)
        {
            using var on = await agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(true), Ct);
            on.EnsureSuccessStatusCode();
        }

        return new World(tags, clock, agent);
    }

    private static PromptRequest Question(string change = "team-auth", string session = "s-1", Guid? project = null) =>
        new(_repo, change, "vm-1", session, PromptKind.Question, Questions: [_store], Project: project?.ToString());

    private static PromptRequest Permission(Guid project, string tool = "Bash") =>
        new(_repo, "team-auth", "vm-1", "s-1", PromptKind.Permission, Tool: tool, Project: project.ToString());

    private static async Task<PromptCreated> AskAsync(World world, PromptRequest request)
    {
        using var response = await world.Agent.PostAsJsonAsync(AgentPromptEndpoints.PromptsPath, request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PromptCreated>(Ct))!;
    }

    private static async Task<(HttpStatusCode Status, PromptWaitResult? Result)> WaitAsync(World world, string promptId, HttpClient? as_ = null)
    {
        using var response = await (as_ ?? world.Agent).GetAsync(AgentPromptEndpoints.WaitPath(Guid.Parse(promptId)), Ct);
        return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PromptWaitResult>(Ct) : null);
    }

    private static async Task<HttpStatusCode> PreviewAsync(World world, string promptId, string preview)
    {
        using var response = await world.Agent.SendApiAsync(HttpMethod.Put, AgentPromptEndpoints.PreviewPath(Guid.Parse(promptId)), new PromptPreview(preview), Ct);
        return response.StatusCode;
    }

    private static async Task<JsonElement?> SessionOnCardAsync(HttpClient browser)
    {
        var card = (await browser.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct)).EnumerateArray()
            .Single(c => c.GetProperty("change").GetString() == "team-auth");
        var sessions = card.GetProperty("sessions").EnumerateArray().ToList();
        return sessions.Count == 0 ? null : Assert.Single(sessions);
    }

    [Fact]
    public async Task A_question_while_not_away_goes_to_the_terminal_and_shows_on_the_card()
    {
        await using var world = await WorldAsync(away: false);

        var created = await AskAsync(world, Question(project: world.Tags.Pan));

        Assert.False(created.Held);
        var session = (await SessionOnCardAsync(world.Tags.Emmz))!.Value;
        Assert.Equal("waiting-on-question", session.GetProperty("state").GetString());
        var detail = session.GetProperty("promptDetail");
        Assert.False(detail.GetProperty("held").GetBoolean());
        Assert.Equal("Which store?", detail.GetProperty("questions")[0].GetProperty("question").GetString());

        // Never held, so the terminal has it: a wait says so at once.
        Assert.Equal(PromptWaitState.Released, (await WaitAsync(world, created.PromptId)).Result!.State);

        // Answered in the terminal, the session moves on, and the question leaves the card.
        using var moved = await world.Agent.PostAsJsonAsync(
            AgentSessionEndpoints.EventsPath,
            new SessionEventRequest(_repo, "team-auth", "vm-1", "s-1", SessionState.Working, "Editing design.md", world.Tags.Pan.ToString()),
            Ct);
        var after = (await SessionOnCardAsync(world.Tags.Emmz))!.Value;
        Assert.Equal("working", after.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("promptDetail").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await WaitAsync(world, created.PromptId)).Status);
    }

    [Fact]
    public async Task A_question_while_away_is_held_and_its_session_events_dont_clear_it()
    {
        await using var world = await WorldAsync(away: true);

        var created = await AskAsync(world, Question(project: world.Tags.Pan));

        Assert.True(created.Held);
        Assert.True((await SessionOnCardAsync(world.Tags.Emmz))!.Value.GetProperty("promptDetail").GetProperty("held").GetBoolean());
        Assert.Equal(PromptWaitState.Pending, (await WaitAsync(world, created.PromptId)).Result!.State);

        using var late = await world.Agent.PostAsJsonAsync(
            AgentSessionEndpoints.EventsPath,
            new SessionEventRequest(_repo, "team-auth", "vm-1", "s-1", SessionState.Working, "late", world.Tags.Pan.ToString()),
            Ct);
        Assert.Equal("waiting-on-question", (await SessionOnCardAsync(world.Tags.Emmz))!.Value.GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_prompt_from_a_session_with_no_card_goes_to_the_terminal_even_while_away()
    {
        await using var world = await WorldAsync(away: true);

        Assert.False((await AskAsync(world, Question(change: "tags", project: world.Tags.Pan))).Held);
    }

    [Fact]
    public async Task A_permission_prompt_carries_its_input_only_once_held()
    {
        await using var world = await WorldAsync(away: false);

        // Not away: the card shows only the tool, to everyone, and a preview is refused.
        var notHeld = await AskAsync(world, Permission(world.Tags.Pan));
        Assert.False(notHeld.Held);
        var session = (await SessionOnCardAsync(world.Tags.Emmz))!.Value;
        Assert.Equal(("permission", "Bash"), (session.GetProperty("prompt").GetProperty("kind").GetString(), session.GetProperty("prompt").GetProperty("tool").GetString()));
        Assert.Equal(JsonValueKind.Null, session.GetProperty("promptDetail").GetProperty("preview").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, await PreviewAsync(world, notHeld.PromptId, "curl -H \"Authorization: Bearer abc\""));

        // Away: held, then the preview arrives, for Emmz only.
        using (var on = await world.Agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(true), Ct))
        {
            on.EnsureSuccessStatusCode();
        }

        var held = await AskAsync(world, Permission(world.Tags.Pan));
        Assert.True(held.Held);
        Assert.Equal(HttpStatusCode.NoContent, await PreviewAsync(world, held.PromptId, "make e2e"));
        Assert.Equal("make e2e", (await SessionOnCardAsync(world.Tags.Emmz))!.Value.GetProperty("promptDetail").GetProperty("preview").GetString());
        var samSees = await world.Tags.Sam.GetStringAsync(DashboardEndpoints.ChangesPath, Ct);
        Assert.DoesNotContain("make e2e", samSees, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"Bash\"", samSees, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.BadRequest, await PreviewAsync(world, held.PromptId, new string('x', PromptLimits.MaxPermissionPreviewBytes + 1)));
    }

    [Fact]
    public async Task A_question_prompt_takes_no_preview_and_unknown_or_others_prompts_are_404()
    {
        await using var world = await WorldAsync(away: true);
        var created = await AskAsync(world, Question(project: world.Tags.Pan));

        Assert.Equal(HttpStatusCode.Conflict, await PreviewAsync(world, created.PromptId, "x"));
        Assert.Equal(HttpStatusCode.NotFound, await PreviewAsync(world, Guid.NewGuid().ToString(), "x"));
        Assert.Equal(HttpStatusCode.NotFound, (await WaitAsync(world, Guid.NewGuid().ToString())).Status);

        using var sam = world.Tags.Factory.CreateClient();
        sam.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await world.Tags.Factory.CreateApiTokenAsync(TagWorld.SamEmail, cancellationToken: Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await WaitAsync(world, created.PromptId, sam)).Status);
    }

    [Fact]
    public async Task Prompts_over_the_limits_or_malformed_are_refused()
    {
        await using var world = await WorldAsync(away: true);
        var huge = new PromptQuestion("Q", "H", [new PromptOption("A", Preview: new string('x', 20 * 1024))]);

        foreach (var bad in new[]
        {
            Question(project: world.Tags.Pan) with { Questions = [huge] },
            Question(project: world.Tags.Pan) with { Questions = [_store, _store, _store, _store, _store] },
            Question(project: world.Tags.Pan) with { SessionId = "" },
            new PromptRequest(_repo, "team-auth", "vm-1", "s-1", PromptKind.Permission),
        })
        {
            using var response = await world.Agent.PostAsJsonAsync(AgentPromptEndpoints.PromptsPath, bad, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Null(await SessionOnCardAsync(world.Tags.Emmz));
    }

    [Fact]
    public async Task Turning_away_off_releases_a_held_prompt_and_wakes_its_wait_at_once()
    {
        await using var world = await WorldAsync(away: true, waitTimeout: "00:00:20");
        var created = await AskAsync(world, Question(project: world.Tags.Pan));

        var started = Stopwatch.StartNew();
        var waiting = WaitAsync(world, created.PromptId);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        using (var off = await world.Agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(false), Ct))
        {
            off.EnsureSuccessStatusCode();
        }

        Assert.Equal(PromptWaitState.Released, (await waiting).Result!.State);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"The wait took {started.Elapsed}.");
        Assert.False((await SessionOnCardAsync(world.Tags.Emmz))!.Value.GetProperty("promptDetail").GetProperty("held").GetBoolean());
    }

    [Fact]
    public async Task A_prompt_the_hook_released_cant_be_answered_from_the_dashboard()
    {
        await using var world = await WorldAsync(away: true);
        var created = await AskAsync(world, Question(project: world.Tags.Pan));
        var promptId = Guid.Parse(created.PromptId);

        using (var released = await world.Agent.DeleteAsync(AgentPromptEndpoints.PromptPath(promptId), Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        }

        using (var answered = await world.Tags.Emmz.PostApiAsync(
            PromptAnswerEndpoints.AnswerPath(promptId),
            new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } },
            Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, answered.StatusCode);
        }

        Assert.Equal(PromptWaitState.Released, (await WaitAsync(world, created.PromptId)).Result!.State);
        using var unknown = await world.Agent.DeleteAsync(AgentPromptEndpoints.PromptPath(Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task A_wait_returns_as_soon_as_an_answer_lands()
    {
        await using var world = await WorldAsync(away: true, waitTimeout: "00:00:20");
        var created = await AskAsync(world, Question(project: world.Tags.Pan));

        var started = Stopwatch.StartNew();
        var waiting = WaitAsync(world, created.PromptId);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        using (var answered = await world.Tags.Emmz.PostApiAsync(
            PromptAnswerEndpoints.AnswerPath(Guid.Parse(created.PromptId)),
            new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } },
            Ct))
        {
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        }

        var result = (await waiting).Result!;
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"The wait took {started.Elapsed}.");
        Assert.Equal((PromptWaitState.Answered, "Memory"), (result.State, result.Answer!.Answers!["Which store?"]));
    }

    [Fact]
    public async Task A_held_prompt_is_released_after_8_hours()
    {
        await using var world = await WorldAsync(away: true, waitGrace: HookAlwaysWaiting);
        var created = await AskAsync(world, Question(project: world.Tags.Pan));

        world.Clock.Advance(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1));
        world.Store.Sweep();
        Assert.True((await SessionOnCardAsync(world.Tags.Emmz))!.Value.GetProperty("promptDetail").GetProperty("held").GetBoolean());

        world.Clock.Advance(TimeSpan.FromSeconds(1));
        world.Store.Sweep();
        Assert.Equal(PromptWaitState.Released, (await WaitAsync(world, created.PromptId)).Result!.State);
    }

    [Fact]
    public async Task A_held_prompt_with_no_wait_for_90_seconds_is_released()
    {
        await using var world = await WorldAsync(away: true);
        var created = await AskAsync(world, Question(project: world.Tags.Pan));
        await WaitAsync(world, created.PromptId);

        world.Clock.Advance(TimeSpan.FromSeconds(89));
        world.Store.Sweep();
        Assert.True((await SessionOnCardAsync(world.Tags.Emmz))!.Value.GetProperty("promptDetail").GetProperty("held").GetBoolean());

        world.Clock.Advance(TimeSpan.FromSeconds(2));
        world.Store.Sweep();
        Assert.Equal(PromptWaitState.Released, (await WaitAsync(world, created.PromptId)).Result!.State);
    }

    // spec agent-presence, A held prompt keeps its session: "Away overnight", then "Released, then quiet".
    [Fact]
    public async Task A_session_holding_a_prompt_overnight_stays_on_its_card_and_expires_only_after_its_release()
    {
        await using var world = await WorldAsync(away: true, waitGrace: HookAlwaysWaiting);
        await AskAsync(world, Question(project: world.Tags.Pan));

        for (var hour = 0; hour < 5; hour++)
        {
            world.Clock.Advance(TimeSpan.FromHours(1));
            world.Store.Sweep();
        }

        var overnight = (await SessionOnCardAsync(world.Tags.Emmz))!.Value;
        Assert.True(overnight.GetProperty("promptDetail").GetProperty("held").GetBoolean());

        using (var off = await world.Agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(false), Ct))
        {
            off.EnsureSuccessStatusCode();
        }

        world.Clock.Advance(TimeSpan.FromHours(4) - TimeSpan.FromMinutes(1));
        world.Store.Sweep();
        Assert.NotNull(await SessionOnCardAsync(world.Tags.Emmz));

        world.Clock.Advance(TimeSpan.FromMinutes(1));
        world.Store.Sweep();
        Assert.Null(await SessionOnCardAsync(world.Tags.Emmz));
    }

    // visit-requests: a visit request is a third kind of prompt, held and released as the others are.
    private static readonly VisitRequest _prVisit = new("https://github.com/emmz/pan/pull/12", "Review the PR", "Approve it if happy");

    private static PromptRequest Visit(Guid project, VisitRequest? visit = null, string change = "team-auth") =>
        new(_repo, change, "vm-1", "s-1", PromptKind.Visit, Project: project.ToString(), Visit: visit ?? _prVisit);

    [Fact]
    public async Task A_visit_request_while_away_is_held_with_its_url_reason_and_action()
    {
        await using var world = await WorldAsync(away: true);

        var created = await AskAsync(world, Visit(world.Tags.Pan));

        Assert.True(created.Held);
        var session = (await SessionOnCardAsync(world.Tags.Emmz))!.Value;
        Assert.Equal("waiting-on-visit", session.GetProperty("state").GetString());
        Assert.Equal("visit", session.GetProperty("prompt").GetProperty("kind").GetString());
        var detail = session.GetProperty("promptDetail");
        Assert.True(detail.GetProperty("held").GetBoolean());
        var visit = detail.GetProperty("visit");
        Assert.Equal(
            ("https://github.com/emmz/pan/pull/12", "Review the PR", "Approve it if happy"),
            (visit.GetProperty("url").GetString(), visit.GetProperty("reason").GetString(), visit.GetProperty("action").GetString()));
        Assert.Equal(PromptWaitState.Pending, (await WaitAsync(world, created.PromptId)).Result!.State);
    }

    [Fact]
    public async Task A_visit_request_while_not_away_or_with_no_card_is_not_held()
    {
        await using (var atDesk = await WorldAsync(away: false))
        {
            var created = await AskAsync(atDesk, Visit(atDesk.Tags.Pan));
            Assert.False(created.Held);
            Assert.Equal(PromptWaitState.Released, (await WaitAsync(atDesk, created.PromptId)).Result!.State);
        }

        await using var away = await WorldAsync(away: true);
        Assert.False((await AskAsync(away, Visit(away.Tags.Pan, change: "tags"))).Held);
    }

    [Fact]
    public async Task Visit_requests_over_the_limits_or_malformed_are_refused()
    {
        await using var world = await WorldAsync(away: true);

        foreach (var bad in new[]
        {
            Visit(world.Tags.Pan, _prVisit with { Url = "file:///Users/emmz/report.html" }),
            Visit(world.Tags.Pan, _prVisit with { Url = "https://bob:secret@example.com/" }),
            Visit(world.Tags.Pan, _prVisit with { Reason = "" }),
            Visit(world.Tags.Pan, _prVisit with { Action = new string('a', PromptLimits.MaxVisitActionLength + 1) }),
            Visit(world.Tags.Pan) with { Visit = null },
            Visit(world.Tags.Pan) with { Tool = "Bash" },
        })
        {
            using var response = await world.Agent.PostAsJsonAsync(AgentPromptEndpoints.PromptsPath, bad, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Null(await SessionOnCardAsync(world.Tags.Emmz));
    }

    [Fact]
    public async Task A_held_visit_request_is_released_by_away_off_after_8_hours_and_without_a_wait()
    {
        // Away off: released at once, and the card no longer offers to answer it.
        await using (var off = await WorldAsync(away: true))
        {
            var created = await AskAsync(off, Visit(off.Tags.Pan));
            using (var turnOff = await off.Agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(false), Ct))
            {
                turnOff.EnsureSuccessStatusCode();
            }

            Assert.Equal(PromptWaitState.Released, (await WaitAsync(off, created.PromptId)).Result!.State);
            Assert.False((await SessionOnCardAsync(off.Tags.Emmz))!.Value.GetProperty("promptDetail").GetProperty("held").GetBoolean());
        }

        await using (var overnight = await WorldAsync(away: true, waitGrace: HookAlwaysWaiting))
        {
            var created = await AskAsync(overnight, Visit(overnight.Tags.Pan));
            overnight.Clock.Advance(TimeSpan.FromHours(8));
            overnight.Store.Sweep();
            Assert.Equal(PromptWaitState.Released, (await WaitAsync(overnight, created.PromptId)).Result!.State);
        }

        await using var lost = await WorldAsync(away: true);
        var asked = await AskAsync(lost, Visit(lost.Tags.Pan));
        await WaitAsync(lost, asked.PromptId);
        lost.Clock.Advance(TimeSpan.FromSeconds(91));
        lost.Store.Sweep();
        Assert.Equal(PromptWaitState.Released, (await WaitAsync(lost, asked.PromptId)).Result!.State);
    }

    [Fact]
    public async Task A_visit_request_takes_no_preview()
    {
        await using var world = await WorldAsync(away: true);
        var created = await AskAsync(world, Visit(world.Tags.Pan));

        Assert.Equal(HttpStatusCode.Conflict, await PreviewAsync(world, created.PromptId, "anything"));
    }
}
