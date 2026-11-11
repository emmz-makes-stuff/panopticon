using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D8; spec change-documents, Answers given from the dashboard.
public class ChangePageAnswersTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string BobEmail = "bob@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RepoRef _repo = new("github.com", "emmz", "panopticon");

    [Fact]
    public async Task The_page_lists_answers_newest_first_with_questions_only_for_those_who_could_answer_and_live()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.Emmz.InviteToProjectAsync(world.Pan, BobEmail, ProjectRoles.Owner, Ct);
        using var bob = await world.Factory.CreateSignedInClientAsync(BobEmail, Ct);
        var changeId = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        using var agent = world.Factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await world.Factory.MemberTokenAsync(Ct));
        agent.DefaultRequestHeaders.Add(ApiRequests.RequestHeader, "1");
        (await agent.SendApiAsync(HttpMethod.Put, AwayEndpoints.AgentPath, new AwayState(true), Ct)).Dispose();

        // Bob allows a Bash permission on Emmz's session.
        var permission = await AskAsync(agent, new PromptRequest(_repo, "team-auth", "vm-1", "s-1", PromptKind.Permission, Tool: "Bash", Project: world.Pan.ToString()));
        (await bob.PostApiAsync(PromptAnswerEndpoints.AnswerPath(permission), new { allow = true }, Ct)).Dispose();

        // Sam's page is open while Emmz answers a question.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var stream = await world.Sam.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(timeout.Token));
        var question = await AskAsync(agent, new PromptRequest(
            _repo, "team-auth", "vm-1", "s-2", PromptKind.Question,
            [new PromptQuestion("Which store?", "Store", [new PromptOption("Memory"), new PromptOption("Postgres")])],
            Project: world.Pan.ToString()));
        (await world.Emmz.PostApiAsync(PromptAnswerEndpoints.AnswerPath(question), new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } }, Ct)).Dispose();

        var live = JsonDocument.Parse(await SseReading.NextEventAsync(reader, DashboardEndpoints.AnswerRecordedEvent, timeout.Token)).RootElement;
        Assert.Equal(changeId, live.GetProperty("changeId").GetGuid());
        Assert.False(live.TryGetProperty("questions", out var liveQuestions) && liveQuestions.ValueKind != JsonValueKind.Null);

        var path = $"{DashboardEndpoints.ChangesPath}/{changeId}";
        var bobs = (await bob.GetFromJsonAsync<JsonElement>(path, Ct)).GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal(["question", "permission"], bobs.Select(a => a.GetProperty("kind").GetString()));
        Assert.Equal("Memory", bobs[0].GetProperty("questions")[0].GetProperty("answer").GetString());
        Assert.Equal(("Bash", "allow"), (bobs[1].GetProperty("tool").GetString(), bobs[1].GetProperty("outcome").GetString()));
        Assert.Equal("bob", bobs[1].GetProperty("answeredBy").GetString());
        Assert.Equal("member", bobs[1].GetProperty("sessionMember").GetString());

        var sams = (await world.Sam.GetFromJsonAsync<JsonElement>(path, Ct)).GetProperty("answers");
        Assert.Equal(2, sams.GetArrayLength());
        Assert.DoesNotContain("Which store?", sams.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Memory", sams.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"answeredBy\":\"member\"", sams.GetRawText(), StringComparison.Ordinal);
    }

    private static async Task<Guid> AskAsync(HttpClient agent, PromptRequest request)
    {
        using var response = await agent.PostAsJsonAsync(AgentPromptEndpoints.PromptsPath, request, Ct);
        var created = (await response.Content.ReadFromJsonAsync<PromptCreated>(Ct))!;
        Assert.True(created.Held);
        return Guid.Parse(created.PromptId);
    }
}
