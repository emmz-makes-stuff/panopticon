using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Hook;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;
using static Panopticon.Cli.Tests.Hook.HookCommandTests;

namespace Panopticon.Cli.Tests.Hook;

// agent-interaction D6, D7; spec remote-answers: the hook's side of "A question while not away", "A prompt while away",
// "Permission input stays local unless held", "Releasing a prompt", "Question forms" and "Prompt limits", and
// agent-presence "A question is not a permission prompt", against a stub server. PromptHookServerTests uses a real one.
public sealed class PromptHookCommandTests : IDisposable
{
    private const string Command = "curl -H 'Authorization: Bearer pnp_secret' https://api.example";

    private static readonly Guid _promptId = Guid.Parse("0193f7a0-0000-7000-8000-000000000001");

    private readonly string _configRoot = Directory.CreateTempSubdirectory("panopticon-prompt-hook-").FullName;
    private readonly StringWriter _stdout = new();
    private readonly TempGitRepo _repo = TempGitRepo.Create();

    public PromptHookCommandTests() => _repo.Git("checkout", "--quiet", "-b", "change/team-auth");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CliConfigFile Config => new(Path.Combine(_configRoot, "panopticon", "config.json"));

    private HookLog Log => HookLog.NextTo(Config, TimeProvider.System);

    private string LogText => File.Exists(Log.Path) ? File.ReadAllText(Log.Path) : "";

    public void Dispose()
    {
        _repo.Dispose();
        Directory.Delete(_configRoot, recursive: true);
    }

    private Task<int> RunAsync(StubServer server, string stdin, CancellationToken? cancellationToken = null) =>
        new HookCommand(() => new HttpClient(server), new BridgeSettings("https://panopticon.example/", "pnp_token", "/", "vm-1"), WorktreeLinksFile.NextTo(Config), Log, _stdout, TimeProvider.System, FastWait)
            .RunAsync(new StringReader(stdin), cancellationToken ?? Ct);

    private static JsonObject Question(string text, string header, bool multiSelect, params (string Label, string? Preview)[] options) => new()
    {
        ["question"] = text,
        ["header"] = header,
        ["multiSelect"] = multiSelect,
        ["options"] = new JsonArray([.. options.Select(o => (JsonNode)new JsonObject { ["label"] = o.Label, ["description"] = $"{o.Label} it is", ["preview"] = o.Preview })]),
    };

    private string AskEvent(params JsonObject[] questions) =>
        Event("PreToolUse", _repo.Root, HookCommand.AskUserQuestion, new JsonObject { ["questions"] = new JsonArray([.. questions]) });

    private string PermissionEvent(string tool = "Bash") =>
        Event("PermissionRequest", _repo.Root, tool, new JsonObject { ["command"] = Command, ["description"] = "Call the API" });

    private static string Created(bool held) => $$"""{ "promptId": "{{_promptId}}", "held": {{(held ? "true" : "false")}} }""";

    private static string Waited(string state, string? answer = null) =>
        answer is null ? $$"""{ "state": "{{state}}" }""" : $$"""{ "state": "{{state}}", "answer": {{answer}} }""";

    /// <summary>A server that holds the prompt when <paramref name="held"/>, then answers each wait from the queue.</summary>
    private static StubServer Server(bool held, params (HttpStatusCode Status, string Body)[] waits)
    {
        var queue = new Queue<(HttpStatusCode, string)>(waits);
        return new StubServer(HttpStatusCode.NoContent)
        {
            Respond = request => request switch
            {
                { Method.Method: "POST", Url: var url } when url.EndsWith("/api/agent/prompts", StringComparison.Ordinal) => (HttpStatusCode.OK, Created(held)),
                { Method.Method: "GET" } when queue.TryDequeue(out var next) => next,
                { Method.Method: "GET" } => (HttpStatusCode.OK, Waited("pending")),
                _ => null,
            },
        };
    }

    [Fact]
    public async Task A_question_while_not_away_is_posted_and_the_terminal_asks()
    {
        var server = Server(held: false);
        var before = DateTimeOffset.UtcNow;

        Assert.Equal(0, await RunAsync(server, AskEvent(Question("Which store?", "Store", false, ("Memory", null), ("Postgres", null)))));

        Assert.Equal("", _stdout.ToString());
        var posted = JsonSerializer.Deserialize(Assert.Single(server.Requests).Body, ContractsJsonContext.Default.PromptRequest)!;
        Assert.Equal((PromptKind.Question, "team-auth", "session-1"), (posted.Kind, posted.Change, posted.SessionId));
        Assert.InRange(posted.OccurredAt!.Value, before, DateTimeOffset.UtcNow);
        var question = Assert.Single(posted.Questions!);
        Assert.Equal(("Which store?", "Store", false), (question.Question, question.Header, question.MultiSelect));
        Assert.Equal([new PromptOption("Memory", "Memory it is"), new PromptOption("Postgres", "Postgres it is")], question.Options);
    }

    [Fact]
    public async Task A_permission_prompt_while_not_away_sends_only_its_tool_name()
    {
        var server = Server(held: false);

        Assert.Equal(0, await RunAsync(server, PermissionEvent()));

        Assert.Equal("", _stdout.ToString());
        var request = Assert.Single(server.Requests);
        Assert.DoesNotContain("pnp_secret", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("api.example", request.Body, StringComparison.Ordinal);
        var posted = JsonSerializer.Deserialize(request.Body, ContractsJsonContext.Default.PromptRequest)!;
        Assert.Equal((PromptKind.Permission, "Bash", null), (posted.Kind, posted.Tool, posted.Questions));
    }

    [Fact]
    public async Task A_permission_request_for_a_question_is_ignored()
    {
        var server = Server(held: true);

        Assert.Equal(0, await RunAsync(server, PermissionEvent(HookCommand.AskUserQuestion)));

        Assert.Empty(server.Requests);
        Assert.Equal("", _stdout.ToString());
    }

    [Fact]
    public async Task A_held_question_waits_and_answers_with_the_labels_injected_into_its_input()
    {
        var answer = """{ "answeredBy": "Bob", "answers": { "Which parts?": "Hooks, Web", "Which store?": "Neither, use SQLite" } }""";
        var server = Server(held: true, (HttpStatusCode.OK, Waited("pending")), (HttpStatusCode.OK, Waited("answered", answer)));
        var parts = Question("Which parts?", "Parts", true, ("Hooks", null), ("Web", null), ("CLI", null));
        var store = Question("Which store?", "Store", false, ("Memory", "```\nnew InMemory()\n```"), ("Postgres", null));

        Assert.Equal(0, await RunAsync(server, AskEvent(parts, store)));

        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests.Skip(1), r => Assert.Equal($"https://panopticon.example/api/agent/prompts/{_promptId}/wait", r.Url));
        var output = JsonNode.Parse(_stdout.ToString())!["hookSpecificOutput"]!;
        Assert.Equal(("PreToolUse", "allow"), (output["hookEventName"]!.GetValue<string>(), output["permissionDecision"]!.GetValue<string>()));
        Assert.Equal("Answered by Bob via Panopticon.", output["additionalContext"]!.GetValue<string>());
        var updated = output["updatedInput"]!;
        Assert.Equal(2, updated["questions"]!.AsArray().Count);
        Assert.Equal("Hooks, Web", updated["answers"]!["Which parts?"]!.GetValue<string>());
        Assert.Equal("Neither, use SQLite", updated["answers"]!["Which store?"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_held_permission_prompt_uploads_its_preview_and_a_deny_carries_the_message()
    {
        var answer = """{ "answeredBy": "Emmz", "allow": false, "message": "not now, the image is stale" }""";
        var server = Server(held: true, (HttpStatusCode.OK, Waited("answered", answer)));

        Assert.Equal(0, await RunAsync(server, PermissionEvent()));

        Assert.Equal(["POST", "PUT", "GET"], server.Requests.Select(r => r.Method.Method));
        var preview = server.Requests[1];
        Assert.Equal(($"https://panopticon.example/api/agent/prompts/{_promptId}/preview", "1"), (preview.Url, preview.RequestHeader));
        Assert.Equal(Command, JsonSerializer.Deserialize(preview.Body, ContractsJsonContext.Default.PromptPreview)!.Preview);
        var output = JsonNode.Parse(_stdout.ToString())!["hookSpecificOutput"]!;
        Assert.Equal("PermissionRequest", output["hookEventName"]!.GetValue<string>());
        Assert.Equal("deny", output["decision"]!["behavior"]!.GetValue<string>());
        Assert.Equal("Denied by Emmz via Panopticon: not now, the image is stale", output["decision"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_allow_runs_the_tool_once_with_no_permission_rule()
    {
        var server = Server(held: true, (HttpStatusCode.OK, Waited("answered", """{ "answeredBy": "Emmz", "allow": true }""")));

        Assert.Equal(0, await RunAsync(server, PermissionEvent()));

        var output = JsonNode.Parse(_stdout.ToString())!["hookSpecificOutput"]!.AsObject();
        Assert.Equal("allow", output["decision"]!["behavior"]!.GetValue<string>());
        Assert.False(output["decision"]!.AsObject().ContainsKey("message"));
        Assert.False(output.ContainsKey("updatedPermissions"));
        Assert.False(output["decision"]!.AsObject().ContainsKey("updatedPermissions"));
    }

    [Fact]
    public async Task A_preview_that_doesnt_reach_the_server_still_waits()
    {
        var server = new StubServer(HttpStatusCode.NoContent)
        {
            Respond = request => request.Method.Method switch
            {
                "POST" => (HttpStatusCode.OK, Created(held: true)),
                "PUT" => (HttpStatusCode.Conflict, """{ "detail": "Only a held permission prompt takes a preview." }"""),
                _ => (HttpStatusCode.OK, Waited("released")),
            },
        };

        Assert.Equal(0, await RunAsync(server, PermissionEvent()));

        Assert.Equal(["POST", "PUT", "GET"], server.Requests.Select(r => r.Method.Method));
        Assert.Contains("the dashboard can only deny", LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{ "state": "released" }""")]
    [InlineData(HttpStatusCode.NotFound, "")]
    public async Task A_released_or_unknown_prompt_goes_to_the_terminal(HttpStatusCode status, string body)
    {
        var server = Server(held: true, (HttpStatusCode.OK, Waited("pending")), (status, body));

        Assert.Equal(0, await RunAsync(server, AskEvent(Question("Q", "H", false, ("A", null)))));

        Assert.Equal("", _stdout.ToString());
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task Losing_the_server_for_the_loss_limit_gives_the_prompt_to_the_terminal()
    {
        var failures = Enumerable.Repeat((HttpStatusCode.BadGateway, ""), 1000).ToArray();
        var server = Server(held: true, failures);
        var started = TimeProvider.System.GetTimestamp();

        Assert.Equal(0, await RunAsync(server, AskEvent(Question("Q", "H", false, ("A", null)))));

        Assert.Equal("", _stdout.ToString());
        Assert.InRange(TimeProvider.System.GetElapsedTime(started), FastWait.ServerLossLimit, FastWait.ServerLossLimit * 4);
        Assert.Contains("lost the server for 1s", LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wait_that_comes_back_after_a_failure_keeps_waiting()
    {
        var answer = """{ "answeredBy": "Emmz", "answers": { "Q": "A" } }""";
        var server = Server(held: true, (HttpStatusCode.BadGateway, ""), (HttpStatusCode.OK, Waited("answered", answer)));

        Assert.Equal(0, await RunAsync(server, AskEvent(Question("Q", "H", false, ("A", null)))));

        Assert.Equal("A", JsonNode.Parse(_stdout.ToString())!["hookSpecificOutput"]!["updatedInput"]!["answers"]!["Q"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_wait_whose_connection_drops_mid_body_is_retried_and_keeps_waiting()
    {
        var answer = """{ "answeredBy": "Emmz", "answers": { "Q": "A" } }""";
        var dropped = 0;
        var waits = new Queue<(HttpStatusCode, string)>([(HttpStatusCode.OK, Waited("answered", answer))]);
        var server = new StubServer(HttpStatusCode.NoContent)
        {
            Respond = request => request switch
            {
                { Method.Method: "POST", Url: var url } when url.EndsWith("/api/agent/prompts", StringComparison.Ordinal) => (HttpStatusCode.OK, Created(held: true)),
                { Method.Method: "GET" } when waits.TryDequeue(out var next) => next,
                _ => null,
            },
            DropAfterHeaders = request => request.Method == HttpMethod.Get && Interlocked.Increment(ref dropped) == 1,
        };

        Assert.Equal(0, await RunAsync(server, AskEvent(Question("Q", "H", false, ("A", null)))));

        Assert.Equal("A", JsonNode.Parse(_stdout.ToString())!["hookSpecificOutput"]!["updatedInput"]!["answers"]!["Q"]!.GetValue<string>());
        Assert.Equal(2, server.Requests.Count(r => r.Method == HttpMethod.Get));
    }

    [Fact]
    public async Task A_preview_upload_that_runs_out_of_time_releases_the_held_prompt_for_the_terminal()
    {
        var server = new StubServer(HttpStatusCode.NoContent)
        {
            Respond = request => request switch
            {
                { Method.Method: "POST", Url: var url } when url.EndsWith("/api/agent/prompts", StringComparison.Ordinal) => (HttpStatusCode.OK, Created(held: true)),
                _ => null,
            },
            HangWhen = request => request.Url.EndsWith("/preview", StringComparison.Ordinal),
        };

        Assert.Equal(0, await RunAsync(server, PermissionEvent()));

        Assert.Equal("", _stdout.ToString());
        var released = server.Requests.Last();
        Assert.Equal((HttpMethod.Delete, $"https://panopticon.example/api/agent/prompts/{_promptId}"), (released.Method, released.Url));
        Assert.Equal("1", released.RequestHeader);
        Assert.DoesNotContain(server.Requests, r => r.Url.EndsWith("/wait", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_cancelled_during_the_wait_releases_the_held_prompt()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var server = new StubServer(HttpStatusCode.NoContent)
        {
            Respond = request => request switch
            {
                { Method.Method: "POST", Url: var url } when url.EndsWith("/api/agent/prompts", StringComparison.Ordinal) => (HttpStatusCode.OK, Created(held: true)),
                { Method.Method: "GET" } => Cancelled(),
                _ => null,
            },
        };

        (HttpStatusCode, string) Cancelled()
        {
            cancel.Cancel();
            return (HttpStatusCode.OK, Waited("pending"));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(server, AskEvent(Question("Q", "H", false, ("A", null))), cancel.Token));

        var released = server.Requests.Last();
        Assert.Equal((HttpMethod.Delete, $"https://panopticon.example/api/agent/prompts/{_promptId}"), (released.Method, released.Url));
    }

    [Fact]
    public async Task A_question_over_the_limits_goes_to_the_terminal_and_the_card_shows_it_waiting()
    {
        var server = Server(held: true);
        var huge = Question("Which design?", "Design", false, ("A", new string('x', 20 * 1024)), ("B", null));

        Assert.Equal(0, await RunAsync(server, AskEvent(huge)));

        var request = Assert.Single(server.Requests);
        Assert.EndsWith("/api/agent/sessions/events", request.Url, StringComparison.Ordinal);
        var sent = JsonSerializer.Deserialize(request.Body, ContractsJsonContext.Default.SessionEventRequest)!;
        Assert.Equal((SessionState.WaitingOnQuestion, null), (sent.State, sent.Activity));
        Assert.Contains("a preview is over 8 KB", LogText, StringComparison.Ordinal);
        Assert.Equal("", _stdout.ToString());
    }

    [Fact]
    public async Task A_question_whose_input_cant_be_read_goes_to_the_terminal()
    {
        var server = Server(held: true);

        Assert.Equal(0, await RunAsync(server, Event("PreToolUse", _repo.Root, HookCommand.AskUserQuestion, new JsonObject { ["questions"] = "?" })));

        var sent = JsonSerializer.Deserialize(Assert.Single(server.Requests).Body, ContractsJsonContext.Default.SessionEventRequest)!;
        Assert.Equal(SessionState.WaitingOnQuestion, sent.State);
    }

    [Fact]
    public async Task The_server_refusing_the_prompt_leaves_the_terminal_to_ask()
    {
        var server = new StubServer(HttpStatusCode.BadRequest, """{ "detail": "This prompt can't go to the dashboard." }""");

        Assert.Equal(0, await RunAsync(server, PermissionEvent()));

        Assert.Equal("", _stdout.ToString());
        Assert.Contains("can't go to the dashboard", LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_preview_is_a_commands_text_or_the_input_as_JSON_cut_to_2_KB()
    {
        var bash = JsonDocument.Parse($$"""{ "command": "{{new string('c', 5000)}}" }""").RootElement;
        Assert.Equal(PromptLimits.MaxPermissionPreviewBytes, PromptHookOutput.Preview("Bash", bash).Length);

        var write = JsonDocument.Parse("""{ "file_path": "/src/a.md", "content": "hi" }""").RootElement;
        Assert.Equal("""{"file_path":"/src/a.md","content":"hi"}""", PromptHookOutput.Preview("Write", write).ReplaceLineEndings("").Replace(" ", "", StringComparison.Ordinal));
        Assert.Equal("", PromptHookOutput.Preview("Write", null));
    }
}
