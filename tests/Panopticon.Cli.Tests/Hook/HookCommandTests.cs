using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Git;
using Panopticon.Cli.Hook;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;

namespace Panopticon.Cli.Tests.Hook;

// agent-interaction D1, D2, D3; spec agent-presence: "Session events from hooks", "Session states" and "Hooks never
// break the session", in process against a stub server. HookCliTests drives the published binary.
public sealed class HookCommandTests : IDisposable
{
    private const string Token = "pnp_hook_token";

    private readonly string _configRoot = Directory.CreateTempSubdirectory("panopticon-hook-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CliConfigFile Config => new(Path.Combine(_configRoot, "panopticon", "config.json"));

    private HookLog Log => HookLog.NextTo(Config, TimeProvider.System);

    private string LogText => File.Exists(Log.Path) ? File.ReadAllText(Log.Path) : "";

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    internal static string Event(string name, string cwd, string? tool = null, JsonObject? input = null)
    {
        var json = new JsonObject { ["hook_event_name"] = name, ["session_id"] = "session-1", ["cwd"] = cwd, ["transcript_path"] = "/tmp/t.jsonl" };
        if (tool is not null)
        {
            json["tool_name"] = tool;
            json["tool_input"] = input ?? [];
            json["tool_use_id"] = "toolu_1";
        }

        return json.ToJsonString();
    }

    private readonly StringWriter _stdout = new();

    // Short limits, so losing the server takes a second, not a minute.
    internal static readonly PromptWaitOptions FastWait = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5));

    private HookCommand Hook(StubServer server, string? token = Token) =>
        new(() => new HttpClient(server), new BridgeSettings("https://panopticon.example/base/", token, "/", "vm-1"), WorktreeLinksFile.NextTo(Config), Log, _stdout, TimeProvider.System, FastWait);

    private static TempGitRepo ChangeRepo(string branch = "change/team-auth")
    {
        var repo = TempGitRepo.Create();
        repo.Git("checkout", "--quiet", "-b", branch);
        return repo;
    }

    [Fact]
    public async Task A_tool_starting_sends_working_with_its_summary_and_never_the_command()
    {
        using var repo = ChangeRepo();
        var server = new StubServer(HttpStatusCode.NoContent);
        var input = new JsonObject { ["command"] = "curl -H 'Authorization: Bearer pnp_secret' x", ["description"] = "Run make gates" };

        var exit = await Hook(server).RunAsync(new StringReader(Event("PreToolUse", repo.Root, "Bash", input)), Ct);

        Assert.Equal(0, exit);
        var request = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Post, "https://panopticon.example/base/api/agent/sessions/events"), (request.Method, request.Url));
        Assert.Equal(($"Bearer {Token}", "1"), (request.Authorization, request.RequestHeader));
        Assert.DoesNotContain("pnp_secret", request.Body, StringComparison.Ordinal);
        var sent = JsonSerializer.Deserialize(request.Body, ContractsJsonContext.Default.SessionEventRequest)!;
        Assert.Equal(new RepoRef("github.com", "emmz", "panopticon"), sent.Repo);
        Assert.Equal(("team-auth", "vm-1", "session-1", SessionState.Working, "Run make gates"), (sent.Change, sent.Machine, sent.SessionId, sent.State, sent.Activity));
        Assert.Equal("", LogText);
    }

    [Fact]
    public async Task Off_a_change_branch_the_change_comes_from_the_bridges_record()
    {
        using var repo = ChangeRepo("main");
        repo.WriteTasks("tags", "## 1. Setup\n- [ ] 1.1 First\n");
        repo.WriteTasks("projects", "## 1. Setup\n- [ ] 1.1 First\n");
        WorktreeLinksFile.NextTo(Config).Record(await GitRepository.FindRootAsync(repo.Root, Ct), "tags", DateTimeOffset.UnixEpoch);
        var server = new StubServer(HttpStatusCode.NoContent);

        await Hook(server).RunAsync(new StringReader(Event("Stop", repo.Root)), Ct);

        var sent = JsonSerializer.Deserialize(Assert.Single(server.Requests).Body, ContractsJsonContext.Default.SessionEventRequest)!;
        Assert.Equal(("tags", SessionState.WaitingOnUser), (sent.Change, sent.State));
    }

    [Fact]
    public async Task With_no_change_or_an_event_it_doesnt_use_nothing_is_sent_or_logged()
    {
        using var onMain = ChangeRepo("main");
        using var onChange = ChangeRepo();
        var server = new StubServer(HttpStatusCode.NoContent);

        Assert.Equal(0, await Hook(server).RunAsync(new StringReader(Event("PreToolUse", onMain.Root, "Bash")), Ct));
        Assert.Equal(0, await Hook(server).RunAsync(new StringReader(Event("PreCompact", onChange.Root)), Ct));
        Assert.Equal(0, await Hook(server).RunAsync(new StringReader(Event("Notification", onChange.Root)), Ct));

        Assert.Empty(server.Requests);
        Assert.Equal("", LogText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "hook_event_name": "Stop" }""")]
    public async Task Unreadable_input_is_logged_and_nothing_is_sent(string stdin)
    {
        var server = new StubServer(HttpStatusCode.NoContent);

        Assert.Equal(0, await Hook(server).RunAsync(new StringReader(stdin), Ct));

        Assert.Empty(server.Requests);
        Assert.Contains("isn't a Claude Code hook event", LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "panopticon login")]
    [InlineData(HttpStatusCode.BadRequest, "Give the change's name")]
    [InlineData(HttpStatusCode.InternalServerError, "(500)")]
    public async Task A_refusal_is_logged_without_the_token(HttpStatusCode status, string expected)
    {
        using var repo = ChangeRepo();
        var server = new StubServer(status, """{ "detail": "Give the change's name." }""");

        Assert.Equal(0, await Hook(server).RunAsync(new StringReader(Event("UserPromptSubmit", repo.Root)), Ct));

        Assert.Contains("hook UserPromptSubmit:", LogText, StringComparison.Ordinal);
        Assert.Contains(expected, LogText, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_logged_in_is_logged_and_nothing_is_sent()
    {
        using var repo = ChangeRepo();
        var server = new StubServer(HttpStatusCode.NoContent);

        Assert.Equal(0, await Hook(server, token: null).RunAsync(new StringReader(Event("Stop", repo.Root)), Ct));

        Assert.Empty(server.Requests);
        Assert.Contains("panopticon login", LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_never_answers_is_given_up_on_within_the_budget()
    {
        using var repo = ChangeRepo();
        var server = new StubServer(HttpStatusCode.NoContent) { Hang = true };
        var started = TimeProvider.System.GetTimestamp();

        Assert.Equal(0, await Hook(server).RunAsync(new StringReader(Event("Stop", repo.Root)), Ct));

        Assert.InRange(TimeProvider.System.GetElapsedTime(started), HookCommand.Budget - TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
        Assert.Contains("gave up after 4s", LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PreToolUse", "Read", SessionState.Working, "Reading a file")]
    [InlineData("PostToolUse", "Read", SessionState.Working, "Reading a file")]
    [InlineData("PostToolUse", HookCommand.AskUserQuestion, SessionState.Working, null)]
    [InlineData("UserPromptSubmit", null, SessionState.Working, null)]
    [InlineData("Stop", null, SessionState.WaitingOnUser, null)]
    [InlineData("SessionEnd", null, SessionState.Ended, null)]
    public void Each_async_event_sets_its_state(string name, string? tool, SessionState state, string? activity)
    {
        var update = HookCommand.StateFor(HookEvent.Parse(Event(name, "/src", tool))!);

        Assert.Equal((state, activity), update);
    }

    [Theory]
    [InlineData("PreToolUse", HookCommand.AskUserQuestion)]
    [InlineData("PreToolUse", null)]
    [InlineData("PermissionRequest", "Bash")]
    [InlineData("PermissionRequest", HookCommand.AskUserQuestion)]
    [InlineData("PreCompact", null)]
    [InlineData("SubagentStop", null)]
    public void Prompts_and_unused_events_send_no_session_event(string name, string? tool)
    {
        Assert.Null(HookCommand.StateFor(HookEvent.Parse(Event(name, "/src", tool))!));
    }

    internal sealed record SeenRequest(HttpMethod Method, string Url, string? Authorization, string? RequestHeader, string Body);

    /// <summary>
    /// Records every request. Answers with <see cref="Respond"/> when it gives a response for the request, else with
    /// the default status and body.
    /// </summary>
    internal sealed class StubServer(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        private readonly Lock _gate = new();

        public List<SeenRequest> Requests { get; } = [];

        public bool Hang { get; init; }

        public Func<SeenRequest, (HttpStatusCode Status, string Body)?>? Respond { get; init; }

        /// <summary>When it says so, the response's headers arrive, then the connection drops part way into the body.</summary>
        public Func<SeenRequest, bool>? DropAfterHeaders { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var seen = new SeenRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Panopticon-Request", out var values) ? values.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            lock (_gate)
            {
                Requests.Add(seen);
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (DropAfterHeaders?.Invoke(seen) == true)
            {
                var dropped = new StreamContent(new DroppingStream("""{ "state": """u8.ToArray()));
                dropped.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                dropped.Headers.ContentLength = 64;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = dropped };
            }

            var (answerStatus, answerBody) = Respond?.Invoke(seen) ?? (status, body);
            return new HttpResponseMessage(answerStatus) { Content = new StringContent(answerBody, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>A body that gives its first bytes, then fails the way a socket reset does.</summary>
    private sealed class DroppingStream(byte[] start) : MemoryStream(start)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            Position < Length ? base.Read(buffer, offset, count) : throw new IOException("The connection was reset.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position < Length ? base.ReadAsync(buffer, cancellationToken) : throw new IOException("The connection was reset.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
