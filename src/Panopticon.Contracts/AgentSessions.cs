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
/// </summary>
public sealed record SessionEventRequest(
    RepoRef Repo,
    string Change,
    string Machine,
    string SessionId,
    SessionState State,
    string? Activity = null,
    string? Project = null);

/// <summary>A member's away switch (D8): the body and answer of <c>/api/agent/away</c> and <c>/api/me/away</c>.</summary>
/// <param name="Since">When away was turned on. The server sets it in its answers and ignores it in a request.</param>
public sealed record AwayState(bool Away, DateTimeOffset? Since = null);

/// <summary>What a session event may carry.</summary>
public static class SessionLimits
{
    /// <summary>A session's activity is one line of at most 120 characters (spec agent-presence, Activity text).</summary>
    public const int MaxActivityLength = 120;
}
