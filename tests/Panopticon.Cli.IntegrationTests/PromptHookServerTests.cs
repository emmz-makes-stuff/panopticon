using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Hook;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;
using Panopticon.Server.Data;
using Panopticon.Server.Presence;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// agent-interaction D3, D6, D7; spec remote-answers: the hook against a real server, from the prompt to the answer
// given on the dashboard. Emmz owns PAN and her sessions report team-auth into it.
public sealed class PromptHookServerTests(PostgresFixture postgres, S3Fixture s3) : IDisposable
{
    private const string Command = "curl -H \"Authorization: Bearer abc-secret\" https://api.example";

    // Long enough for a test to answer, short enough that a lost server doesn't stall the run.
    private static readonly PromptWaitOptions _wait = new(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30));

    private readonly string _configRoot = Directory.CreateTempSubdirectory("panopticon-prompt-server-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    private sealed record World(TagWorld Tags, TempGitRepo Repo, Guid ChangeId, string Token, Recorder Requests) : IAsyncDisposable
    {
        public PresenceStore Presence => Tags.Factory.Services.GetRequiredService<PresenceStore>();

        public async ValueTask DisposeAsync()
        {
            Repo.Dispose();
            await Tags.DisposeAsync();
        }
    }

    private async Task<World> CreateAsync(bool away)
    {
        var tags = await TagWorld.CreateAsync(postgres, s3, Ct, settings: new Dictionary<string, string>
        {
            ["Panopticon:Prompts:WaitTimeout"] = "00:00:01",
        });
        var changeId = await tags.ReportChangeAsync(TagWorld.Report("team-auth", tags.Pan), Ct);
        var repo = TempGitRepo.Create("git@github.com:emmz/panopticon.git");
        repo.Git("checkout", "--quiet", "-b", "change/team-auth");
        repo.WriteFile(ProjectLink.FileName, $$"""{ "project": "{{tags.Pan}}" }""");
        if (away)
        {
            await SetAwayAsync(tags, true);
        }

        return new World(tags, repo, changeId, await tags.Factory.MemberTokenAsync(Ct), new Recorder());
    }

    private static async Task SetAwayAsync(TagWorld tags, bool away)
    {
        using var response = await tags.Emmz.SendApiAsync(HttpMethod.Put, AwayEndpoints.MePath, new AwayState(away), Ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Runs the hook in process against the test server, returning what it wrote to stdout.</summary>
    private Task<string> RunHookAsync(World world, string stdin) => Task.Run(async () =>
    {
        var stdout = new StringWriter();
        var settings = new BridgeSettings("http://localhost/", world.Token, world.Repo.Root, "vm-1");
        var config = new CliConfigFile(Path.Combine(_configRoot, "panopticon", "config.json"));
        var hook = new HookCommand(
            () => new HttpClient(new RecordingHandler(world.Requests, world.Tags.Factory.Server.CreateHandler())),
            settings,
            WorktreeLinksFile.NextTo(config),
            HookLog.NextTo(config, TimeProvider.System),
            stdout,
            TimeProvider.System,
            _wait);
        Assert.Equal(0, await hook.RunAsync(new StringReader(stdin), Ct));
        return stdout.ToString();
    });

    private static string Event(string name, string cwd, string tool, JsonObject input) => new JsonObject
    {
        ["hook_event_name"] = name,
        ["session_id"] = "session-1",
        ["cwd"] = cwd,
        ["tool_name"] = tool,
        ["tool_input"] = input,
    }.ToJsonString();

    private static string StoreQuestion(string cwd) => Event("PreToolUse", cwd, HookCommand.AskUserQuestion, new JsonObject
    {
        ["questions"] = new JsonArray(new JsonObject
        {
            ["question"] = "Which store?",
            ["header"] = "Store",
            ["multiSelect"] = false,
            ["options"] = new JsonArray(new JsonObject { ["label"] = "Memory" }, new JsonObject { ["label"] = "Postgres" }),
        }),
    });

    private static string BashPermission(string cwd) =>
        Event("PermissionRequest", cwd, "Bash", new JsonObject { ["command"] = Command, ["description"] = "Call the API" });

    /// <summary>The session's view once <paramref name="ready"/> holds, within a few seconds.</summary>
    private static async Task<SessionView> SessionWhenAsync(World world, Func<SessionView, bool> ready)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (world.Presence.For(world.ChangeId).SingleOrDefault() is { } session && ready(session))
            {
                return session;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("The session never reached the expected state.");
    }

    [Fact]
    public async Task Not_away_a_permission_prompt_shows_on_the_card_and_its_command_never_leaves_the_machine()
    {
        await using var world = await CreateAsync(away: false);

        Assert.Equal("", await RunHookAsync(world, BashPermission(world.Repo.Root)));

        var session = Assert.Single(world.Presence.For(world.ChangeId));
        Assert.Equal((SessionState.WaitingOnPermission, "Bash", false), (session.State, session.Prompt?.Tool, session.PromptDetail?.Held));
        Assert.Null(session.PromptDetail?.Preview);
        Assert.NotEmpty(world.Requests.Bodies);
        Assert.All(world.Requests.Bodies, body =>
        {
            Assert.DoesNotContain("abc-secret", body, StringComparison.Ordinal);
            Assert.DoesNotContain("api.example", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Away_a_question_answered_on_the_dashboard_reaches_the_session_and_the_terminal_never_asks()
    {
        await using var world = await CreateAsync(away: true);
        var hook = RunHookAsync(world, StoreQuestion(world.Repo.Root));

        var session = await SessionWhenAsync(world, s => s.PromptDetail is { Held: true });
        Assert.False(hook.IsCompleted);
        using (var answered = await world.Tags.Emmz.PostApiAsync(
            PromptAnswerEndpoints.AnswerPath(session.PromptDetail!.Id), new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } }, Ct))
        {
            answered.EnsureSuccessStatusCode();
        }

        var output = JsonNode.Parse(await hook.WaitAsync(TimeSpan.FromSeconds(30), Ct))!["hookSpecificOutput"]!;
        Assert.Equal("allow", output["permissionDecision"]!.GetValue<string>());
        Assert.Equal("Memory", output["updatedInput"]!["answers"]!["Which store?"]!.GetValue<string>());
        Assert.StartsWith("Answered by ", output["additionalContext"]!.GetValue<string>(), StringComparison.Ordinal);
        await using var db = world.Tags.Factory.NewDb();
        var row = await db.RemoteAnswers.SingleAsync(Ct);
        Assert.Equal((RemoteAnswer.QuestionKind, world.ChangeId), (row.Kind, row.ChangeId));
    }

    [Fact]
    public async Task Away_a_permission_prompts_preview_arrives_once_held_and_a_deny_reaches_the_session()
    {
        await using var world = await CreateAsync(away: true);
        var hook = RunHookAsync(world, BashPermission(world.Repo.Root));

        var session = await SessionWhenAsync(world, s => s.PromptDetail is { Held: true, Preview: not null });
        Assert.Equal(Command, session.PromptDetail!.Preview);
        using (var denied = await world.Tags.Emmz.PostApiAsync(
            PromptAnswerEndpoints.AnswerPath(session.PromptDetail.Id), new { allow = false, message = "not now, the image is stale" }, Ct))
        {
            denied.EnsureSuccessStatusCode();
        }

        var decision = JsonNode.Parse(await hook.WaitAsync(TimeSpan.FromSeconds(30), Ct))!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("deny", decision["behavior"]!.GetValue<string>());
        Assert.EndsWith("via Panopticon: not now, the image is stale", decision["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(SessionState.Working, Assert.Single(world.Presence.For(world.ChangeId)).State);
    }

    [Fact]
    public async Task Turning_away_off_gives_a_held_prompt_back_to_the_terminal_within_seconds()
    {
        await using var world = await CreateAsync(away: true);
        var hook = RunHookAsync(world, StoreQuestion(world.Repo.Root));
        var session = await SessionWhenAsync(world, s => s.PromptDetail is { Held: true });

        await SetAwayAsync(world.Tags, false);

        Assert.Equal("", await hook.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        using var late = await world.Tags.Emmz.PostApiAsync(
            PromptAnswerEndpoints.AnswerPath(session.PromptDetail!.Id), new { answers = new Dictionary<string, string> { ["Which store?"] = "Memory" } }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, late.StatusCode);
    }

    // visit-requests D1, D2: the hook holds request_visit, and its output is the input the bridge's tool then runs with.
    private const string PrUrl = "https://github.com/emmz/pan/pull/12";

    private static string PrVisit(string cwd) => Event("PreToolUse", cwd, "mcp__plugin_panopticon_panopticon__request_visit", new JsonObject
    {
        ["url"] = PrUrl,
        ["reason"] = "Review the PR",
        ["action"] = "Approve it if happy",
    });

    [Fact]
    public async Task Away_a_visit_answered_on_the_dashboard_reaches_the_tool_as_done_with_the_note_and_who_answered()
    {
        await using var world = await CreateAsync(away: true);
        var hook = RunHookAsync(world, PrVisit(world.Repo.Root));

        var session = await SessionWhenAsync(world, s => s.PromptDetail is { Held: true });
        Assert.Equal((SessionState.WaitingOnVisit, PrUrl), (session.State, session.PromptDetail!.Visit!.Url));
        Assert.False(hook.IsCompleted);
        using (var answered = await world.Tags.Emmz.PostApiAsync(
            PromptAnswerEndpoints.AnswerPath(session.PromptDetail.Id), new { visited = true, message = "approved" }, Ct))
        {
            answered.EnsureSuccessStatusCode();
        }

        var output = JsonNode.Parse(await hook.WaitAsync(TimeSpan.FromSeconds(30), Ct))!["hookSpecificOutput"]!;
        Assert.Equal("allow", output["permissionDecision"]!.GetValue<string>());

        // Claude Code runs the tool with updatedInput: the bridge turns it into the agent's answer.
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(
            new BridgeSettings(null, null, repo.Root, "vm-1"), () => new HttpClient(), "claude-code", Ct);
        var arguments = output["updatedInput"]!.AsObject().ToDictionary(p => p.Key, p => (object?)p.Value);
        var result = await bridge.Client.CallToolAsync(ContractsAssembly.RequestVisitToolName, arguments, cancellationToken: Ct);
        Assert.Matches(@"^Done: approved\. Answered by .+ via Panopticon\.$", BridgeHarness.Text(result));

        await using var db = world.Tags.Factory.NewDb();
        var row = await db.RemoteAnswers.SingleAsync(Ct);
        Assert.Equal((RemoteAnswer.VisitKind, RemoteAnswer.Done, PrUrl), (row.Kind, row.Outcome, row.Url));
    }

    [Fact]
    public async Task Not_away_or_after_away_off_a_visit_writes_nothing_so_the_tool_says_ask_in_the_terminal()
    {
        await using var world = await CreateAsync(away: false);
        Assert.Equal("", await RunHookAsync(world, PrVisit(world.Repo.Root)).WaitAsync(TimeSpan.FromSeconds(10), Ct));

        await SetAwayAsync(world.Tags, true);
        var held = RunHookAsync(world, PrVisit(world.Repo.Root));
        var session = await SessionWhenAsync(world, s => s.PromptDetail is { Held: true });
        await SetAwayAsync(world.Tags, false);

        Assert.Equal("", await held.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        using var late = await world.Tags.Emmz.PostApiAsync(PromptAnswerEndpoints.AnswerPath(session.PromptDetail!.Id), new { visited = true }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task Panopticon_away_flips_the_members_flag_on_the_server()
    {
        await using var world = await CreateAsync(away: false);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var away = new AwayCommand(
            () => new HttpClient(world.Tags.Factory.Server.CreateHandler()),
            new BridgeSettings("http://localhost/", world.Token, world.Repo.Root, "vm-1"),
            stdout,
            stderr);
        var memberId = await world.Tags.Factory.MemberIdAsync(cancellationToken: Ct);

        Assert.Equal(0, await away.RunAsync(["on"], Ct));
        await using (var db = world.Tags.Factory.NewDb())
        {
            Assert.NotNull(await db.Members.Where(m => m.Id == memberId).Select(m => m.AwaySince).SingleAsync(Ct));
        }

        Assert.Equal(0, await away.RunAsync([], Ct));
        Assert.Equal(0, await away.RunAsync(["off"], Ct));
        await using (var db = world.Tags.Factory.NewDb())
        {
            Assert.Null(await db.Members.Where(m => m.Id == memberId).Select(m => m.AwaySince).SingleAsync(Ct));
        }

        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Away since ", lines[0], StringComparison.Ordinal);
        Assert.Equal(lines[0], lines[1]);
        Assert.StartsWith("Not away", lines[2], StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());
    }

    /// <summary>Every request body the hook sent.</summary>
    private sealed class Recorder
    {
        private readonly Lock _gate = new();
        private readonly List<string> _bodies = [];

        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (_gate)
                {
                    return [.. _bodies];
                }
            }
        }

        public void Add(string body)
        {
            lock (_gate)
            {
                _bodies.Add(body);
            }
        }
    }

    private sealed class RecordingHandler(Recorder recorder, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            recorder.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
