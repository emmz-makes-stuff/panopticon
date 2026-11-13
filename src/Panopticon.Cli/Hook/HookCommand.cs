using System.Text.Json;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Git;
using Panopticon.Contracts;

namespace Panopticon.Cli.Hook;

/// <summary>One Claude Code hook event, as read from stdin: only the fields the hook uses.</summary>
internal sealed record HookEvent(string EventName, string SessionId, string Cwd, string? ToolName, JsonElement? ToolInput)
{
    /// <summary>The event in <paramref name="json"/>, or null when it isn't one (not JSON, or a field is missing).</summary>
    public static HookEvent? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "hook_event_name") is not { } eventName
                || Text(root, "session_id") is not { } sessionId
                || Text(root, "cwd") is not { } cwd)
            {
                return null;
            }

            JsonElement? toolInput = root.TryGetProperty("tool_input", out var input) ? input.Clone() : null;
            return new HookEvent(eventName, sessionId, cwd, Text(root, "tool_name"), toolInput);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}

/// <summary>How long the sync hook waits on a held prompt (agent-interaction D6). Tests shorten these.</summary>
/// <param name="HoldLimit">The longest a prompt is waited on in all: 8 hours, inside Claude Code's 30000s timeout.</param>
/// <param name="ServerLossLimit">How long with no successful <c>wait</c> before the terminal takes over: 60s.</param>
/// <param name="RetryDelay">The pause after a failed <c>wait</c>.</param>
/// <param name="WaitCallTimeout">
/// One <c>wait</c> call's limit: the server's 25s long-poll and 5s to spare. Two hung calls then use up the 60s, so a
/// lost server hands the prompt back in about a minute, not two.
/// </param>
internal sealed record PromptWaitOptions(TimeSpan HoldLimit, TimeSpan ServerLossLimit, TimeSpan RetryDelay, TimeSpan WaitCallTimeout)
{
    public static PromptWaitOptions Default { get; } =
        new(TimeSpan.FromHours(8), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
}

/// <summary>
/// <c>panopticon hook</c> (agent-interaction D1, D2, D6, D7, D9): reads one Claude Code hook event from stdin and
/// sends the session's new state, with the same server and token as reports. Whatever happens it exits 0, and a
/// failure goes to the <see cref="HookLog"/>, never to stdout (spec agent-presence, Hooks never break the session).
/// <list type="bullet">
/// <item>The async events finish within <see cref="Budget"/>, with no output.</item>
/// <item>A question (<c>PreToolUse</c> on <c>AskUserQuestion</c>) or a permission prompt (<c>PermissionRequest</c>) is
/// posted; when the server holds it, the hook waits for an answer and writes D7's output. Otherwise, and on any
/// failure, it writes nothing and the terminal asks as usual.</item>
/// </list>
/// </summary>
internal sealed partial class HookCommand(
    Func<HttpClient> createHttpClient,
    BridgeSettings settings,
    WorktreeLinksFile? links,
    HookLog log,
    TextWriter stdout,
    TimeProvider clock,
    PromptWaitOptions? waitOptions = null)
{
    public const string AskUserQuestion = "AskUserQuestion";

    /// <summary>
    /// How long an event may take, network and git included, inside the spec's 5 seconds: the rest is process
    /// start-up and reading stdin. A held prompt's wait is outside it.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);

    private readonly PromptWaitOptions _wait = waitOptions ?? PromptWaitOptions.Default;

    /// <summary>When this run began (one run per hook process): the event's <c>OccurredAt</c>, which orders it (F2).</summary>
    private DateTimeOffset _startedAt;

    private sealed record Target(AgentApiClient Client, RepoRef Repo, string Change, string? Project);

    public async Task<int> RunAsync(TextReader stdin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stdin);
        _startedAt = clock.GetUtcNow();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        string? eventName = null;
        HttpClient? http = null;
        try
        {
            var hookEvent = HookEvent.Parse(await stdin.ReadToEndAsync(budget.Token));
            if (hookEvent is null)
            {
                log.Write("hook: the input isn't a Claude Code hook event (JSON with hook_event_name, session_id and cwd); nothing was sent.");
                return 0;
            }

            eventName = hookEvent.EventName;
            var kind = PromptKindFor(hookEvent);
            var update = kind is null ? StateFor(hookEvent) : null;
            if (kind is null && update is null)
            {
                return 0;
            }

            http = createHttpClient();
            if (await ResolveTargetAsync(hookEvent, http, budget.Token) is not { } target)
            {
                return 0;
            }

            if (kind is { } promptKind)
            {
                await AskAsync(hookEvent, promptKind, target, budget, cancellationToken);
            }
            else
            {
                await SendAsync(hookEvent, target, update!.Value.State, update.Value.Activity, budget.Token);
            }
        }
        catch (BridgeException exception)
        {
            log.Write($"hook {eventName ?? "?"}: {exception.Message}");
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            log.Write($"hook {eventName ?? "?"}: gave up after {Budget.TotalSeconds:0}s, so the session's state wasn't sent.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or JsonException)
        {
            log.Write($"hook {eventName ?? "?"}: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            http?.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// The state an asynchronous event sets (spec agent-presence, Session states), or null for an event this path
    /// doesn't send: one it doesn't use, and the prompts (<see cref="PromptKindFor"/>).
    /// </summary>
    internal static (SessionState State, string? Activity)? StateFor(HookEvent hookEvent)
    {
        ArgumentNullException.ThrowIfNull(hookEvent);
        var tool = hookEvent.ToolName;
        return hookEvent.EventName switch
        {
            "PreToolUse" when tool is not null and not AskUserQuestion && !IsVisitTool(tool) => (SessionState.Working, ActivitySummary.For(tool, hookEvent.ToolInput)),
            "PostToolUse" when tool is not null and not AskUserQuestion => (SessionState.Working, ActivitySummary.For(tool, hookEvent.ToolInput)),
            "PostToolUse" => (SessionState.Working, null),
            "UserPromptSubmit" => (SessionState.Working, null),
            "Stop" => (SessionState.WaitingOnUser, null),
            "SessionEnd" => (SessionState.Ended, null),
            _ => null,
        };
    }

    /// <summary>
    /// The prompt an event raises, or null. A <c>PermissionRequest</c> for <c>AskUserQuestion</c> is ignored: Claude
    /// Code raises one for every question the <c>PreToolUse</c> hook doesn't answer (D1, found in §1), and the
    /// question is already posted. <c>PreToolUse</c> on the bridge's <c>request_visit</c> is a visit request
    /// (visit-requests D3), and a <c>PermissionRequest</c> for it is ignored too, as the plugin's matchers do.
    /// </summary>
    internal static PromptKind? PromptKindFor(HookEvent hookEvent)
    {
        ArgumentNullException.ThrowIfNull(hookEvent);
        return hookEvent switch
        {
            { EventName: "PreToolUse", ToolName: AskUserQuestion } => PromptKind.Question,
            { EventName: "PreToolUse", ToolName: { } tool } when IsVisitTool(tool) => PromptKind.Visit,
            { EventName: "PermissionRequest", ToolName: { } tool } when tool != AskUserQuestion && !IsVisitTool(tool) => PromptKind.Permission,
            _ => null,
        };
    }

    /// <summary>
    /// Whether <paramref name="toolName"/> is the bridge's <c>request_visit</c>, by Claude Code's MCP tool name: the
    /// plugin's server (<c>mcp__plugin_panopticon_panopticon__request_visit</c>) or a hand-added one
    /// (<c>mcp__panopticon__request_visit</c>). The plugin's <c>hooks.json</c> uses the same pattern.
    /// </summary>
    internal static bool IsVisitTool(string toolName) => VisitToolPattern().IsMatch(toolName);

    [System.Text.RegularExpressions.GeneratedRegex(@"^mcp__.*panopticon.*__request_visit$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex VisitToolPattern();

    /// <summary>The server, the repo and the change the event is for; null when the session isn't on a change.</summary>
    private async Task<Target?> ResolveTargetAsync(HookEvent hookEvent, HttpClient http, CancellationToken cancellationToken)
    {
        var (baseUrl, token) = settings.RequireApi();
        if (await SessionChange.ResolveAsync(hookEvent.Cwd, links, cancellationToken) is not { } change)
        {
            // Not working on a change: nothing to show, and nothing wrong.
            return null;
        }

        var repo = ProgressReporter.ParseRemote(await GitRepository.GetOriginUrlAsync(change.WorktreeRoot, cancellationToken));
        var project = await ProjectLink.ReadAsync(change.WorktreeRoot, cancellationToken);
        return new Target(new AgentApiClient(http, baseUrl, token), repo, change.Change, project?.ToString());
    }

    private Task SendAsync(HookEvent hookEvent, Target target, SessionState state, string? activity, CancellationToken cancellationToken) =>
        target.Client.SendEventAsync(
            new SessionEventRequest(target.Repo, target.Change, settings.MachineName, hookEvent.SessionId, state, activity, target.Project, _startedAt),
            cancellationToken);

    /// <summary>
    /// Posts the prompt (spec remote-answers, A prompt while away). A permission prompt carries only its tool's name
    /// until held. One that doesn't fit the limits, or can't be read, isn't posted: the card shows the session waiting,
    /// and the terminal asks. When held, a permission prompt's preview goes up, then the hook waits.
    /// </summary>
    private async Task AskAsync(HookEvent hookEvent, PromptKind kind, Target target, CancellationTokenSource budget, CancellationToken cancellationToken)
    {
        var waiting = kind switch
        {
            PromptKind.Question => SessionState.WaitingOnQuestion,
            PromptKind.Visit => SessionState.WaitingOnVisit,
            _ => SessionState.WaitingOnPermission,
        };
        var questions = kind == PromptKind.Question ? PromptHookOutput.Questions(hookEvent.ToolInput) : null;
        var visit = kind == PromptKind.Visit ? PromptHookOutput.Visit(hookEvent.ToolInput) : null;
        var request = new PromptRequest(
            target.Repo, target.Change, settings.MachineName, hookEvent.SessionId, kind, questions, kind == PromptKind.Permission ? hookEvent.ToolName : null, target.Project, _startedAt, visit);
        var problem = (kind, questions, visit) switch
        {
            (PromptKind.Question, null, _) => "its questions can't be read",
            (PromptKind.Visit, _, null) => "its url and reason can't be read",
            _ => PromptLimits.Problem(request),
        };
        if (problem is not null)
        {
            // A visit request the tool will refuse at once isn't waiting on anyone: the session is just running a tool.
            if (kind == PromptKind.Visit)
            {
                await SendAsync(hookEvent, target, SessionState.Working, ActivitySummary.For(hookEvent.ToolName!, hookEvent.ToolInput), budget.Token);
                log.Write($"hook {hookEvent.EventName}: this visit request wasn't sent to the dashboard: {problem}.");
                return;
            }

            await SendAsync(hookEvent, target, waiting, null, budget.Token);
            log.Write($"hook {hookEvent.EventName}: this prompt went to the terminal, not the dashboard: {problem}.");
            return;
        }

        var created = await target.Client.AskAsync(request, budget.Token);
        if (!created.Held || !Guid.TryParse(created.PromptId, out var promptId))
        {
            return;
        }

        PromptAnswer? answer;
        try
        {
            if (kind == PromptKind.Permission)
            {
                await SendPreviewAsync(hookEvent, target.Client, promptId, budget.Token);
            }

            answer = await WaitAsync(target.Client, promptId, hookEvent.EventName, cancellationToken);
        }
        catch
        {
            // Giving up on a held prompt (the budget ran out, or the run was cancelled): the terminal asks, so no one
            // may answer it in vain. The wait's own exits need nothing: the server releases at the hold limit, and
            // after a lost server it can't be told.
            await ReleaseAsync(target.Client, promptId, hookEvent.EventName);
            throw;
        }

        if (answer is not null && PromptHookOutput.For(kind, hookEvent.ToolInput, answer) is { } output)
        {
            await stdout.WriteAsync(output);
            await stdout.FlushAsync(cancellationToken);
        }
    }

    /// <summary>How long the best-effort release of a prompt may take, once the event's budget is spent.</summary>
    internal static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Uploads a held permission prompt's preview; one that doesn't arrive leaves the dashboard only Deny.</summary>
    private async Task SendPreviewAsync(HookEvent hookEvent, AgentApiClient client, Guid promptId, CancellationToken cancellationToken)
    {
        try
        {
            await client.SetPreviewAsync(promptId, PromptHookOutput.Preview(hookEvent.ToolName!, hookEvent.ToolInput), cancellationToken);
        }
        catch (BridgeException exception)
        {
            log.Write($"hook {hookEvent.EventName}: the preview didn't reach the server, so the dashboard can only deny: {exception.Message}");
        }
    }

    /// <summary>
    /// Gives a held prompt back to the terminal on the server (code review F3); a failure is only logged. It has its
    /// own timeout, as the run's token may be what was cancelled.
    /// </summary>
    private async Task ReleaseAsync(AgentApiClient client, Guid promptId, string eventName)
    {
        using var timeout = new CancellationTokenSource(ReleaseTimeout, clock);
        try
        {
            await client.ReleaseAsync(promptId, timeout.Token);
        }
        catch (Exception exception) when (exception is BridgeException or OperationCanceledException)
        {
            log.Write($"hook {eventName}: couldn't release the held prompt, so the server keeps it until its wait grace passes: {exception.Message}");
        }
    }

    /// <summary>
    /// Waits on a held prompt (D6): the answer, or null once it's released, unknown to the server (404), past the hold
    /// limit, or the server has gone unanswered for <see cref="PromptWaitOptions.ServerLossLimit"/>.
    /// </summary>
    private async Task<PromptAnswer?> WaitAsync(AgentApiClient client, Guid promptId, string eventName, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var lastHeard = started;
        while (clock.GetElapsedTime(started) < _wait.HoldLimit)
        {
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            call.CancelAfter(_wait.WaitCallTimeout);
            try
            {
                var result = await client.WaitAsync(promptId, call.Token);
                lastHeard = clock.GetTimestamp();
                switch (result?.State)
                {
                    case PromptWaitState.Answered when result.Answer is { } answer:
                        return answer;
                    case PromptWaitState.Pending:
                        continue;
                    default:
                        // Released, or the server doesn't know it (404): the terminal asks.
                        return null;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is BridgeException or OperationCanceledException)
            {
                if (clock.GetElapsedTime(lastHeard) >= _wait.ServerLossLimit)
                {
                    log.Write($"hook {eventName}: lost the server for {_wait.ServerLossLimit.TotalSeconds:0}s, so the terminal asks: {exception.Message}");
                    return null;
                }

                await Task.Delay(_wait.RetryDelay, clock, cancellationToken);
            }
        }

        log.Write($"hook {eventName}: no answer within {_wait.HoldLimit.TotalHours:0} hours, so the terminal asks.");
        return null;
    }
}
