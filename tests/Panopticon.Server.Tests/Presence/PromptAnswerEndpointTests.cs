using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D5, D6, D8; spec remote-answers: Who may answer, What others see of a prompt, Answers are
// attributed, Permission prompts (server side), Question forms (the answer format).
public class PromptAnswerEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string BobEmail = "bob@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RepoRef _repo = new("github.com", "emmz", "panopticon");

    private static readonly PromptQuestion _store = new("Which store?", "Store", [new PromptOption("Memory"), new PromptOption("Postgres")]);

    private static readonly PromptQuestion _parts = new(
        "Which parts?",
        "Parts",
        [new PromptOption("Hooks"), new PromptOption("Web"), new PromptOption("CLI")],
        MultiSelect: true);

    private sealed class World(TagWorld tags, HttpClient agent, HttpClient bob) : IAsyncDisposable
    {
        public TagWorld Tags { get; } = tags;

        /// <summary>Emmz's session, by her token.</summary>
        public HttpClient Agent { get; } = agent;

        /// <summary>An owner of PAN, signed in.</summary>
        public HttpClient Bob { get; } = bob;

        public async ValueTask DisposeAsync()
        {
            Agent.Dispose();
            Bob.Dispose();
            await Tags.DisposeAsync();
        }
    }

    private async Task<World> WorldAsync()
    {
        var tags = await TagWorld.CreateAsync(postgres, s3, Ct, settings: new Dictionary<string, string>
        {
            [$"{PromptOptions.Section}:WaitTimeout"] = "00:00:01",
        });
        await tags.Emmz.InviteToProjectAsync(tags.Pan, BobEmail, ProjectRoles.Owner, Ct);
        var bob = await tags.Factory.CreateSignedInClientAsync(BobEmail, Ct);
        await tags.ReportChangeAsync(TagWorld.Report("team-auth", tags.Pan), Ct);
        await tags.ReportChangeAsync(TagWorld.Report("scratch", project: null), Ct);
        var agent = tags.Factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await tags.Factory.MemberTokenAsync(Ct));
        agent.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");
        using var on = await agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(true), Ct);
        on.EnsureSuccessStatusCode();
        return new World(tags, agent, bob);
    }

    private static async Task<Guid> AskAsync(World world, PromptRequest request)
    {
        using var response = await world.Agent.PostAsJsonAsync(AgentPromptEndpoints.PromptsPath, request, Ct);
        var created = (await response.Content.ReadFromJsonAsync<PromptCreated>(Ct))!;
        Assert.True(created.Held);
        return Guid.Parse(created.PromptId);
    }

    private static PromptRequest Question(Guid? project, params PromptQuestion[] questions) =>
        new(_repo, project is null ? "scratch" : "team-auth", "vm-1", "s-1", PromptKind.Question, questions, Project: project?.ToString());

    private static PromptRequest Permission(Guid project) =>
        new(_repo, "team-auth", "vm-1", "s-1", PromptKind.Permission, Tool: "Bash", Project: project.ToString());

    private static async Task<HttpResponseMessage> AnswerAsync(HttpClient browser, Guid promptId, object body) =>
        await browser.PostApiAsync(PromptAnswerEndpoints.AnswerPath(promptId), body, Ct);

    private static async Task<PromptWaitResult> WaitAsync(World world, Guid promptId) =>
        (await world.Agent.GetFromJsonAsync<PromptWaitResult>(AgentPromptEndpoints.WaitPath(promptId), Ct))!;

    private static async Task<string> NameAsync(World world, string email)
    {
        await using var db = world.Tags.Factory.NewDb();
        var id = await world.Tags.Factory.MemberIdAsync(email, Ct);
        return await db.Members.Where(m => m.Id == id).Select(m => m.DisplayName).SingleAsync(Ct);
    }

    [Fact]
    public async Task A_Project_owner_answers_and_the_session_is_told_who_and_the_answer_is_recorded()
    {
        await using var world = await WorldAsync();
        var promptId = await AskAsync(world, Question(world.Tags.Pan, _store, _parts));

        using var response = await AnswerAsync(world.Bob, promptId, new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory", ["Which parts?"] = "Hooks, Web" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bob = await NameAsync(world, BobEmail);
        var waited = await WaitAsync(world, promptId);
        Assert.Equal(PromptWaitState.Answered, waited.State);
        Assert.Equal(bob, waited.Answer!.AnsweredBy);
        Assert.Equal("Memory", waited.Answer.Answers!["Which store?"]);
        Assert.Equal("Hooks, Web", waited.Answer.Answers["Which parts?"]);

        await using var db = world.Tags.Factory.NewDb();
        var row = await db.RemoteAnswers.SingleAsync(Ct);
        Assert.Equal(
            (RemoteAnswer.QuestionKind, await world.Tags.Factory.MemberIdAsync(cancellationToken: Ct), await world.Tags.Factory.MemberIdAsync(BobEmail, Ct)),
            (row.Kind, row.SessionMemberId, row.AnsweredByMemberId));
        Assert.Contains("Hooks, Web", row.QuestionsJson, StringComparison.Ordinal);
        Assert.Null(row.ToolName);

        // The session is working again, its prompt gone, and the prompt can't be answered twice.
        var card = (await world.Tags.Emmz.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct)).EnumerateArray()
            .Single(c => c.GetProperty("change").GetString() == "team-auth");
        var session = Assert.Single(card.GetProperty("sessions").EnumerateArray());
        Assert.Equal("working", session.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("prompt").ValueKind);
        using var again = await AnswerAsync(world.Tags.Emmz, promptId, new { answers = new Dictionary<string, string> { ["Which store?"] = "Postgres", ["Which parts?"] = "CLI" } });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task An_editor_cant_answer_and_the_prompt_stays_held()
    {
        await using var world = await WorldAsync();
        var promptId = await AskAsync(world, Question(world.Tags.Pan, _store));

        using var response = await AnswerAsync(world.Tags.Sam, promptId, new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PromptWaitState.Pending, (await WaitAsync(world, promptId)).State);
        await using var db = world.Tags.Factory.NewDb();
        Assert.Equal(0, await db.RemoteAnswers.CountAsync(Ct));
    }

    [Fact]
    public async Task Only_its_member_answers_a_personal_changes_prompt()
    {
        await using var world = await WorldAsync();
        var promptId = await AskAsync(world, Question(project: null, _store));

        using (var bob = await AnswerAsync(world.Bob, promptId, new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, bob.StatusCode);
        }

        using var emmz = await AnswerAsync(world.Tags.Emmz, promptId, new { answers = new Dictionary<string, string> { ["Which store?"] = "Neither, use SQLite" } });
        Assert.Equal(HttpStatusCode.OK, emmz.StatusCode);
        Assert.Equal("Neither, use SQLite", (await WaitAsync(world, promptId)).Answer!.Answers!["Which store?"]);
    }

    [Fact]
    public async Task Two_answers_at_once_give_one_200_and_one_409()
    {
        await using var world = await WorldAsync();
        for (var round = 0; round < 5; round++)
        {
            var promptId = await AskAsync(world, Question(world.Tags.Pan, _store) with { SessionId = $"s-{round}" });
            var body = new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } };

            var answers = await Task.WhenAll(AnswerAsync(world.Tags.Emmz, promptId, body), AnswerAsync(world.Bob, promptId, body));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], answers.Select(a => a.StatusCode).Order());
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        await using var db = world.Tags.Factory.NewDb();
        Assert.Equal(5, await db.RemoteAnswers.CountAsync(Ct));
    }

    [Fact]
    public async Task A_deny_with_a_message_reaches_the_session_and_is_recorded_without_the_input()
    {
        await using var world = await WorldAsync();
        var promptId = await AskAsync(world, Permission(world.Tags.Pan));
        using (var preview = await world.Agent.SendApiAsync(HttpMethod.Put, AgentPromptEndpoints.PreviewPath(promptId), new PromptPreview("make e2e"), Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, preview.StatusCode);
        }

        using var response = await AnswerAsync(world.Tags.Emmz, promptId, new { allow = false, message = "not now, the image is stale" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = (await WaitAsync(world, promptId)).Answer!;
        Assert.Equal((false, "not now, the image is stale"), (answer.Allow, answer.Message));
        await using var db = world.Tags.Factory.NewDb();
        var row = await db.RemoteAnswers.SingleAsync(Ct);
        Assert.Equal((RemoteAnswer.PermissionKind, "Bash", RemoteAnswer.Deny, (string?)null), (row.Kind, row.ToolName, row.Outcome, row.QuestionsJson));
    }

    [Fact]
    public async Task An_allow_carries_no_message()
    {
        await using var world = await WorldAsync();
        var promptId = await AskAsync(world, Permission(world.Tags.Pan));

        using var response = await AnswerAsync(world.Bob, promptId, new { allow = true, message = "ignored" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = (await WaitAsync(world, promptId)).Answer!;
        Assert.Equal((true, (string?)null, (IReadOnlyDictionary<string, string>?)null), (answer.Allow, answer.Message, answer.Answers));
    }

    [Fact]
    public async Task Answers_that_dont_fit_the_prompt_are_refused()
    {
        await using var world = await WorldAsync();
        var question = await AskAsync(world, Question(world.Tags.Pan, _store, _parts));

        foreach (var bad in new object[]
        {
            new { allow = true },
            new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } },
            new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory", ["Which parts?"] = " " } },
            new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory", ["Which parts?"] = "Web", ["Extra?"] = "x" } },
            new { answers = new Dictionary<string, string> { ["Which store?"] = new string('x', 2001), ["Which parts?"] = "Web" } },
        })
        {
            using var response = await AnswerAsync(world.Tags.Emmz, question, bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var permission = await AskAsync(world, Permission(world.Tags.Pan) with { SessionId = "s-2" });
        using (var noAllow = await AnswerAsync(world.Tags.Emmz, permission, new { message = "why" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noAllow.StatusCode);
        }

        Assert.Equal(PromptWaitState.Pending, (await WaitAsync(world, question)).State);
    }

    [Fact]
    public async Task Unknown_released_or_never_held_prompts_cant_be_answered()
    {
        await using var world = await WorldAsync();
        var body = new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } };

        using (var unknown = await AnswerAsync(world.Tags.Emmz, Guid.NewGuid(), body))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        var promptId = await AskAsync(world, Question(world.Tags.Pan, _store));
        using (var off = await world.Agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(false), Ct))
        {
            off.EnsureSuccessStatusCode();
        }

        using (var released = await AnswerAsync(world.Tags.Emmz, promptId, body))
        {
            Assert.Equal(HttpStatusCode.Conflict, released.StatusCode);
        }

        using var created = await world.Agent.PostAsJsonAsync(AgentPromptEndpoints.PromptsPath, Question(world.Tags.Pan, _store) with { SessionId = "s-2" }, Ct);
        var notHeld = (await created.Content.ReadFromJsonAsync<PromptCreated>(Ct))!;
        Assert.False(notHeld.Held);
        using var terminal = await AnswerAsync(world.Tags.Emmz, Guid.Parse(notHeld.PromptId), body);
        Assert.Equal(HttpStatusCode.Conflict, terminal.StatusCode);
    }

    // spec remote-answers "A viewer": no part of the command, or the question, reaches Sam's browser.
    [Fact]
    public async Task Sams_browser_never_gets_the_command_or_the_question_text()
    {
        await using var world = await WorldAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var stream = await world.Tags.Sam.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(timeout.Token));

        var permission = await AskAsync(world, Permission(world.Tags.Pan));
        using (var preview = await world.Agent.SendApiAsync(HttpMethod.Put, AgentPromptEndpoints.PreviewPath(permission), new PromptPreview("curl -H \"Authorization: Bearer abc\""), Ct))
        {
            preview.EnsureSuccessStatusCode();
        }

        var cards = await world.Tags.Sam.GetStringAsync(DashboardEndpoints.ChangesPath, Ct);
        (await AnswerAsync(world.Tags.Emmz, permission, new { allow = false, message = "no" })).Dispose();
        var question = await AskAsync(world, Question(world.Tags.Pan, _store) with { SessionId = "s-2" });
        (await AnswerAsync(world.Bob, question, new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } })).Dispose();

        var lines = new List<string>();
        lines.AddRange(await SseReading.LinesUpToEventAsync(reader, DashboardEndpoints.AnswerRecordedEvent, timeout.Token));
        lines.AddRange(await SseReading.LinesUpToEventAsync(reader, DashboardEndpoints.AnswerRecordedEvent, timeout.Token));

        foreach (var text in lines.Append(cards))
        {
            Assert.DoesNotContain("Bearer abc", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Which store?", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Memory", text, StringComparison.Ordinal);
        }

        Assert.Contains(lines, l => l.Contains("\"tool\":\"Bash\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"outcome\":\"deny\"", StringComparison.Ordinal));
    }
}
