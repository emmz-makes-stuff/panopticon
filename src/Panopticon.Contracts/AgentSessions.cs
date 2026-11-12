using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Contracts;

// The hook endpoints under /api/agent/ (agent-interaction D3). `panopticon hook` sends these with the reporting token;
// the server binds them. Both go through ContractsJsonContext (source-generated, AOT-safe).

/// <summary>
/// A Claude Code session's state (spec agent-presence, Session states). On the wire in kebab case:
/// <c>working</c>, <c>waiting-on-question</c>, <c>waiting-on-permission</c>, <c>waiting-on-user</c>, <c>ended</c>.
/// Numbers and unknown names are refused.
/// </summary>
[JsonConverter(typeof(SessionStateConverter))]
public enum SessionState
{
    /// <summary>Running tools, with an activity (<c>PreToolUse</c>, <c>PostToolUse</c>, <c>UserPromptSubmit</c>).</summary>
    Working,

    /// <summary>Asked with <c>AskUserQuestion</c>.</summary>
    WaitingOnQuestion,

    /// <summary>Met a permission prompt (<c>PermissionRequest</c>).</summary>
    WaitingOnPermission,

    /// <summary>Its turn ended (<c>Stop</c>).</summary>
    WaitingOnUser,

    /// <summary>The session closed (<c>SessionEnd</c>): the server removes it.</summary>
    Ended,
}

/// <summary>Reads and writes <see cref="SessionState"/> by kebab-case name, refusing numbers.</summary>
public sealed class SessionStateConverter()
    : JsonStringEnumConverter<SessionState>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false);

/// <summary>
/// Body of <c>POST /api/agent/sessions/events</c>. <see cref="Activity"/> is the CLI's one-line summary (D2), at most
/// <see cref="SessionLimits.MaxActivityLength"/> characters, and only for <see cref="SessionState.Working"/>.
/// <see cref="OccurredAt"/> is when the hook's process started, on the session's own machine: async hooks can arrive out
/// of order, so the server ignores an event older than the last it applied for the session (code review F2). An older
/// CLI sends none, and its events are applied in the order they arrive.
/// </summary>
public sealed record SessionEventRequest(
    RepoRef Repo,
    string Change,
    string Machine,
    string SessionId,
    SessionState State,
    string? Activity = null,
    string? Project = null,
    DateTimeOffset? OccurredAt = null);

/// <summary>A member's away switch (D8): the body and answer of <c>/api/agent/away</c> and <c>/api/me/away</c>.</summary>
/// <param name="Since">When away was turned on. The server sets it in its answers and ignores it in a request.</param>
public sealed record AwayState(bool Away, DateTimeOffset? Since = null);

/// <summary>The hook endpoints' paths (D3), relative to the server's base URL, shared by the CLI and the server.</summary>
public static class AgentApiPaths
{
    public const string SessionEvents = "api/agent/sessions/events";
    public const string Prompts = "api/agent/prompts";
    public const string Away = "api/agent/away";

    /// <summary>A held prompt: <c>DELETE</c> releases it, when the hook gives up before waiting.</summary>
    public static string Prompt(Guid promptId) => $"{Prompts}/{promptId}";

    public static string Preview(Guid promptId) => $"{Prompts}/{promptId}/preview";

    public static string Wait(Guid promptId) => $"{Prompts}/{promptId}/wait";
}

/// <summary>What a session event may carry.</summary>
public static class SessionLimits
{
    /// <summary>A session's activity is one line of at most 120 characters (spec agent-presence, Activity text).</summary>
    public const int MaxActivityLength = 120;
}
