using Panopticon.Contracts;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Presence;

/// <summary>The active change a session's events name, and its scope (agent-interaction D4).</summary>
public sealed record SessionPlacement(Guid ChangeId, ChangeScope Scope);

/// <summary>
/// One Claude Code session as dashboards see it (spec agent-presence). <see cref="Id"/> is the server's own key for
/// it: Claude Code's session id never leaves the server. <see cref="Since"/> is when the current state and activity
/// began, for the elapsed time on the card.
/// </summary>
public sealed record SessionView(
    Guid Id,
    Guid MemberId,
    string MemberName,
    string Machine,
    SessionState State,
    string? Activity,
    DateTimeOffset Since,
    DateTimeOffset LastEventAt)
{
    /// <summary>What the session is waiting on, as everyone who can see it sees it: the kind, and a permission's tool.</summary>
    public SessionPrompt? Prompt { get; init; }

    /// <summary>
    /// The prompt itself: only for viewers who may answer it (D5). Its presence is what tells the dashboard to say
    /// "Needs you" (spec live-dashboard, Needs you). Never sent to anyone else: see <see cref="For"/>.
    /// </summary>
    public PromptDetail? PromptDetail { get; init; }

    /// <summary>The session as <paramref name="viewer"/> may see it on a change in <paramref name="scope"/>.</summary>
    public SessionView For(Viewer viewer, ChangeScope scope)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return PromptDetail is null || viewer.CanAnswer(MemberId, scope) ? this : this with { PromptDetail = null };
    }
}

/// <summary>The part of a session's prompt every viewer of its change sees: "Needs Emmz: permission for Bash".</summary>
public sealed record SessionPrompt(PromptKind Kind, string? Tool = null);

/// <summary>
/// The part of a prompt only those who may answer see (D5; spec remote-answers, What others see of a prompt).
/// <see cref="Held"/> false means "answer in the terminal". A permission prompt's <see cref="Preview"/> arrives only
/// once it's held (spec remote-answers, Permission input stays local unless held).
/// </summary>
public sealed record PromptDetail(
    Guid Id,
    bool Held,
    DateTimeOffset AskedAt,
    IReadOnlyList<PromptQuestion>? Questions = null,
    string? Preview = null);

/// <summary>A prompt as the answer endpoint sees it before claiming it (D5, D6).</summary>
/// <param name="Placement">The session's change; null when it has no card, so the prompt was never held.</param>
/// <param name="Answerable">Pending, held, and not already being answered.</param>
public sealed record PromptSnapshot(
    Guid PromptId,
    Guid SessionMemberId,
    SessionPlacement? Placement,
    PromptKind Kind,
    IReadOnlyList<PromptQuestion>? Questions,
    string? Tool,
    bool Answerable);
