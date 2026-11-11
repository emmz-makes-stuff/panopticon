using System.Text.Json;
using Panopticon.Contracts;

namespace Panopticon.Cli.Tests;

// The hook-endpoint contracts (agent-interaction D3), round-tripped through the source-generated context the AOT CLI
// uses.
public class AgentContractsTests
{
    private static readonly RepoRef _repo = new("github.com", "emmz-makes-stuff", "panopticon");

    private static readonly PromptQuestion _storeQuestion = new(
        "Which store?",
        "Store",
        [new PromptOption("Memory", "Fast, lost on restart"), new PromptOption("Postgres", Preview: "```sql\nselect 1;\n```")]);

    private static PromptRequest QuestionPrompt(params PromptQuestion[] questions) =>
        new(_repo, "agent-interaction", "vm-1", "s-1", PromptKind.Question, Questions: questions);

    private static T RoundTrip<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(value, typeInfo), typeInfo)!;

    [Fact]
    public void A_session_event_serializes_with_kebab_case_state_and_round_trips()
    {
        var request = new SessionEventRequest(_repo, "agent-interaction", "vm-1", "s-1", SessionState.Working, "Editing tasks.md", "a975addb");

        var json = JsonSerializer.Serialize(request, ContractsJsonContext.Default.SessionEventRequest);

        Assert.Equal(
            """{"repo":{"host":"github.com","owner":"emmz-makes-stuff","name":"panopticon"},"change":"agent-interaction","machine":"vm-1","sessionId":"s-1","state":"working","activity":"Editing tasks.md","project":"a975addb"}""",
            json);
        Assert.Equal(request, RoundTrip(request, ContractsJsonContext.Default.SessionEventRequest));
    }

    [Theory]
    [InlineData(SessionState.Working, "working")]
    [InlineData(SessionState.WaitingOnQuestion, "waiting-on-question")]
    [InlineData(SessionState.WaitingOnPermission, "waiting-on-permission")]
    [InlineData(SessionState.WaitingOnUser, "waiting-on-user")]
    [InlineData(SessionState.Ended, "ended")]
    public void Every_session_state_has_its_wire_name(SessionState state, string name)
    {
        var request = new SessionEventRequest(_repo, "c", "m", "s", state);

        var json = JsonSerializer.Serialize(request, ContractsJsonContext.Default.SessionEventRequest);

        Assert.Contains($"\"state\":\"{name}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("activity", json, StringComparison.Ordinal);
        Assert.Equal(state, RoundTrip(request, ContractsJsonContext.Default.SessionEventRequest).State);
    }

    [Theory]
    [InlineData("\"sleeping\"")]
    [InlineData("\"3\"")]
    [InlineData("3")]
    [InlineData("null")]
    public void An_unknown_session_state_is_refused(string state)
    {
        var json = $$"""{"repo":{"host":"h","owner":"o","name":"n"},"change":"c","machine":"m","sessionId":"s","state":{{state}}}""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, ContractsJsonContext.Default.SessionEventRequest));
    }

    [Fact]
    public void A_question_prompt_round_trips_with_its_options_and_previews()
    {
        var request = QuestionPrompt(_storeQuestion, new PromptQuestion("Which parts?", "Parts", [new PromptOption("Hooks"), new PromptOption("Web")], MultiSelect: true));

        var json = JsonSerializer.Serialize(request, ContractsJsonContext.Default.PromptRequest);
        var back = RoundTrip(request, ContractsJsonContext.Default.PromptRequest);

        Assert.Contains("\"kind\":\"question\"", json, StringComparison.Ordinal);
        Assert.Contains("\"multiSelect\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"tool\"", json, StringComparison.Ordinal);
        // Records compare their lists by reference, so compare each level.
        Assert.Equal(json, JsonSerializer.Serialize(back, ContractsJsonContext.Default.PromptRequest));
        Assert.NotNull(request.Questions);
        Assert.NotNull(back.Questions);
        Assert.Equal(request.Questions.Select(q => (q.Question, q.Header, q.MultiSelect)), back.Questions.Select(q => (q.Question, q.Header, q.MultiSelect)));
        Assert.Equal(request.Questions.SelectMany(q => q.Options), back.Questions.SelectMany(q => q.Options));
        Assert.Equal(request with { Questions = back.Questions }, back);
    }

    [Fact]
    public void A_permission_prompt_carries_only_its_tool_name()
    {
        var request = new PromptRequest(_repo, "c", "vm-1", "s-1", PromptKind.Permission, Tool: "Bash");

        var json = JsonSerializer.Serialize(request, ContractsJsonContext.Default.PromptRequest);

        Assert.Equal(
            """{"repo":{"host":"github.com","owner":"emmz-makes-stuff","name":"panopticon"},"change":"c","machine":"vm-1","sessionId":"s-1","kind":"permission","tool":"Bash"}""",
            json);
        Assert.Equal(request, RoundTrip(request, ContractsJsonContext.Default.PromptRequest));
    }

    [Fact]
    public void An_unknown_prompt_kind_is_refused()
    {
        const string json = """{"repo":{"host":"h","owner":"o","name":"n"},"change":"c","machine":"m","sessionId":"s","kind":"plan"}""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, ContractsJsonContext.Default.PromptRequest));
    }

    [Fact]
    public void Created_preview_and_away_round_trip()
    {
        var created = new PromptCreated("p-1", Held: true);
        var preview = new PromptPreview("make e2e");
        var away = new AwayState(true, new DateTimeOffset(2026, 11, 11, 17, 0, 0, TimeSpan.Zero));

        Assert.Equal("""{"promptId":"p-1","held":true}""", JsonSerializer.Serialize(created, ContractsJsonContext.Default.PromptCreated));
        Assert.Equal(created, RoundTrip(created, ContractsJsonContext.Default.PromptCreated));
        Assert.Equal(preview, RoundTrip(preview, ContractsJsonContext.Default.PromptPreview));
        Assert.Equal(away, RoundTrip(away, ContractsJsonContext.Default.AwayState));
        Assert.Equal("""{"away":false}""", JsonSerializer.Serialize(new AwayState(false), ContractsJsonContext.Default.AwayState));
    }

    [Fact]
    public void Wait_results_round_trip_for_each_state()
    {
        var pending = new PromptWaitResult(PromptWaitState.Pending);
        var question = new PromptWaitResult(
            PromptWaitState.Answered,
            new PromptAnswer("Bob", new Dictionary<string, string> { ["Which parts?"] = "Hooks, Web" }));
        var denied = new PromptWaitResult(PromptWaitState.Answered, new PromptAnswer("Emmz", Allow: false, Message: "not now"));

        Assert.Equal("""{"state":"pending"}""", JsonSerializer.Serialize(pending, ContractsJsonContext.Default.PromptWaitResult));
        Assert.Equal(
            """{"state":"answered","answer":{"answeredBy":"Bob","answers":{"Which parts?":"Hooks, Web"}}}""",
            JsonSerializer.Serialize(question, ContractsJsonContext.Default.PromptWaitResult));
        Assert.Equal("""{"state":"released"}""", JsonSerializer.Serialize(new PromptWaitResult(PromptWaitState.Released), ContractsJsonContext.Default.PromptWaitResult));

        var back = RoundTrip(question, ContractsJsonContext.Default.PromptWaitResult);
        Assert.Equal("Hooks, Web", back.Answer!.Answers!["Which parts?"]);
        Assert.Equal(denied, RoundTrip(denied, ContractsJsonContext.Default.PromptWaitResult));
    }

    [Fact]
    public void An_unknown_wait_state_is_refused() =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize("""{"state":"lost"}""", ContractsJsonContext.Default.PromptWaitResult));

    [Fact]
    public void A_prompt_within_the_limits_has_no_problem()
    {
        Assert.Null(PromptLimits.Problem(QuestionPrompt(_storeQuestion)));
        Assert.Null(PromptLimits.Problem(new PromptRequest(_repo, "c", "m", "s", PromptKind.Permission, Tool: "Bash")));

        var full = new PromptQuestion("Q", "H", [new("A", Preview: new string('x', PromptLimits.MaxPreviewBytes)), new("B"), new("C"), new("D")]);
        Assert.Null(PromptLimits.Problem(QuestionPrompt(full, full, full, full)));
    }

    [Fact]
    public void Prompts_over_a_limit_are_refused()
    {
        var option = new PromptOption("A");
        Assert.NotNull(PromptLimits.Problem(QuestionPrompt(_storeQuestion, _storeQuestion, _storeQuestion, _storeQuestion, _storeQuestion)));
        Assert.NotNull(PromptLimits.Problem(QuestionPrompt(new PromptQuestion("Q", "H", [option, option, option, option, option]))));
        Assert.NotNull(PromptLimits.Problem(QuestionPrompt(new PromptQuestion("Q", "H", [new PromptOption("A", Preview: new string('x', 20 * 1024))]))));

        // Twelve 8000-byte previews are each within their limit, but over 64 KB together.
        var preview = new string('x', 8000);
        var big = new PromptQuestion("Q", "H", [new("A", Preview: preview), new("B", Preview: preview), new("C", Preview: preview)]);
        Assert.Equal("it is over 64 KB", PromptLimits.Problem(QuestionPrompt(big, big, big, big)));
    }

    [Fact]
    public void Malformed_prompts_are_refused()
    {
        Assert.NotNull(PromptLimits.Problem(QuestionPrompt()));
        Assert.NotNull(PromptLimits.Problem(QuestionPrompt(new PromptQuestion("Q", "H", []))));
        Assert.NotNull(PromptLimits.Problem(QuestionPrompt(_storeQuestion) with { Tool = "Bash" }));
        Assert.NotNull(PromptLimits.Problem(new PromptRequest(_repo, "c", "m", "s", PromptKind.Permission)));
        Assert.NotNull(PromptLimits.Problem(new PromptRequest(_repo, "c", "m", "s", PromptKind.Permission, [_storeQuestion], "Bash")));
    }

    [Fact]
    public void A_permission_preview_is_cut_to_2_KB_without_splitting_a_character()
    {
        Assert.Equal("make e2e", PromptLimits.CutPermissionPreview("make e2e"));

        var ascii = PromptLimits.CutPermissionPreview(new string('a', 5000));
        Assert.Equal(PromptLimits.MaxPermissionPreviewBytes, ascii.Length);

        // "é" is 2 bytes and "🙂" is 4 (a surrogate pair): after one leading byte, neither fits the 2048-byte cut evenly.
        var accents = PromptLimits.CutPermissionPreview("a" + new string('é', 2000));
        Assert.Equal(1 + 1023, accents.Length);
        var emoji = PromptLimits.CutPermissionPreview("a" + string.Concat(Enumerable.Repeat("🙂", 1000)));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(emoji) <= PromptLimits.MaxPermissionPreviewBytes);
        Assert.False(char.IsHighSurrogate(emoji[^1]));
        Assert.Equal(1 + (511 * 2), emoji.Length);
    }
}
