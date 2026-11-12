using Microsoft.Extensions.Options;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;

namespace Panopticon.Server.Presence;

/// <summary>
/// How long prompts are held and waited on (agent-interaction D6), and how long the store looks out for a hook's late
/// events. Settings under <see cref="Section"/>.
/// </summary>
public sealed class PromptOptions
{
    public const string Section = "Panopticon:Prompts";

    /// <summary>A held prompt goes back to the terminal after this long unanswered: 8 hours.</summary>
    public TimeSpan HoldLimit { get; set; } = TimeSpan.FromHours(8);

    /// <summary>A held prompt goes back to the terminal when no <c>wait</c> has come for this long (the hook is gone).</summary>
    public TimeSpan WaitGrace { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>How long one <c>wait</c> long-poll waits before answering "pending".</summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How long a stamped <c>SessionEnd</c> turns away older events: 1 minute. A late async hook arrives within seconds,
    /// and a short window means a clock that's behind can't hide a resumed session for long (second review #6).
    /// </summary>
    public TimeSpan LateEventWindow { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>What setting a held permission prompt's preview came to.</summary>
public enum PreviewOutcome
{
    Set,

    /// <summary>The store doesn't know the prompt, or it isn't this member's: 404.</summary>
    NotFound,

    /// <summary>Not a held, pending permission prompt: 409.</summary>
    NotHeld,
}

/// <summary>
/// The sessions working on changes right now, and their prompts (D4, D6; spec agent-presence: Presence is not history).
/// In memory only: a restart loses them, and nothing here touches the database. Sessions are keyed by the reporting
/// member and Claude Code's session id, so two members' ids can never collide. Every change that a dashboard can see is
/// published on the <see cref="ChangeBroadcaster"/>, under the store's lock, so one session's events go out in order.
/// <para>
/// A session has at most one prompt. A held one is pending until answered or released; while it's pending, the
/// session never expires (spec agent-presence, A held prompt keeps its session), and its other events don't move it on.
/// </para>
/// </summary>
public sealed class PresenceStore(
    ChangeBroadcaster broadcaster,
    TimeProvider clock,
    IOptions<ActivityOptions> activity,
    IOptions<PromptOptions> prompts)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid MemberId, string SessionId), Entry> _sessions = [];
    private readonly Dictionary<Guid, Prompt> _prompts = [];

    /// <summary>
    /// Sessions that ended with a stamped <c>SessionEnd</c>: its time, and when the store saw it. A late event from
    /// before the end can't bring the session back, for <see cref="PromptOptions.LateEventWindow"/>.
    /// </summary>
    private readonly Dictionary<(Guid MemberId, string SessionId), (DateTimeOffset OccurredAt, DateTimeOffset SeenAt)> _ended = [];

    private long _sequence;

    // Order: when the store first saw the session, as a counter, so two sessions seen in the same instant keep theirs.
    /// <param name="OccurredAt">The newest stamped event applied to the session, if any.</param>
    private sealed record Entry(SessionPlacement? Placement, SessionView View, long Order, Guid? PromptId = null, DateTimeOffset? OccurredAt = null);

    /// <summary>A prompt's state; changed only under the store's lock.</summary>
    private sealed class Prompt((Guid MemberId, string SessionId) session, PromptRequest request, bool held, DateTimeOffset askedAt)
    {
        public (Guid MemberId, string SessionId) Session { get; } = session;
        public PromptKind Kind { get; } = request.Kind;
        public IReadOnlyList<PromptQuestion>? Questions { get; } = request.Questions;
        public string? Tool { get; } = request.Tool;
        public bool Held { get; } = held;
        public DateTimeOffset AskedAt { get; } = askedAt;
        public DateTimeOffset LastWaitAt { get; set; } = askedAt;

        /// <summary>An answer is being recorded: nothing else may answer or release it until that lands or fails.</summary>
        public bool Claimed { get; set; }

        /// <summary>Pending only while held: a prompt that isn't held is the terminal's from the start.</summary>
        public PromptWaitState State { get; set; } = held ? PromptWaitState.Pending : PromptWaitState.Released;

        public PromptAnswer? Answer { get; set; }

        /// <summary>When it was answered: a detached answer is kept for <see cref="PromptOptions.WaitGrace"/> from then.</summary>
        public DateTimeOffset AnsweredAt { get; set; }

        /// <summary>A wait has read where it ended up, so the hook has its answer.</summary>
        public bool Read { get; set; }

        /// <summary>
        /// Its session moved on before a wait read the answer (code review F4): it's kept, apart from the session, until
        /// a wait reads it or the grace passes.
        /// </summary>
        public bool Detached { get; set; }

        /// <summary>Completed when <see cref="State"/> leaves pending, waking any <c>wait</c>.</summary>
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsPending => State == PromptWaitState.Pending;
    }

    /// <summary>
    /// Records a session's new state. <paramref name="placement"/> is null when its change has no active card: the
    /// session is kept, but shows nowhere. <see cref="SessionState.Ended"/> removes it. Moving to another change ends
    /// it on the old one. Activity is kept only while working. Any event clears a prompt that isn't pending from the
    /// session (spec remote-answers: answering in the terminal, or moving on, clears it), though an answer the hook
    /// hasn't read yet is kept for it; a pending one stays, since the session is paused on it.
    /// <para>
    /// With <paramref name="occurredAt"/> (code review F2), an event older than the last stamped one applied to the
    /// session is ignored, as is one no newer than a stamped <c>SessionEnd</c>. Returns whether it was applied.
    /// </para>
    /// </summary>
    public bool Record(
        Guid memberId,
        string memberName,
        string sessionId,
        SessionPlacement? placement,
        string machine,
        SessionState state,
        string? activity,
        DateTimeOffset? occurredAt = null)
    {
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(machine);
        if (state == SessionState.Ended)
        {
            return End(memberId, sessionId, occurredAt);
        }

        var now = clock.GetUtcNow();
        activity = state == SessionState.Working ? activity : null;
        lock (_gate)
        {
            var key = (memberId, sessionId);
            _sessions.TryGetValue(key, out var existing);
            if (IsStale(key, existing, occurredAt))
            {
                return false;
            }

            _ended.Remove(key);
            // Not stale, so a stamp here is never older than the one before it.
            var stamp = occurredAt ?? existing?.OccurredAt;
            if (existing?.PromptId is { } pendingId && _prompts[pendingId].IsPending)
            {
                Put(key, existing with { View = existing.View with { LastEventAt = now }, OccurredAt = stamp });
                return true;
            }

            DropPrompt(existing);
            var unchanged = existing is not null && existing.View.State == state && existing.View.Activity == activity;
            var view = new SessionView(
                existing?.View.Id ?? Guid.CreateVersion7(),
                memberId,
                memberName,
                machine,
                state,
                activity,
                unchanged ? existing!.View.Since : now,
                now);
            Put(key, new Entry(placement, view, existing?.Order ?? ++_sequence, OccurredAt: stamp), existing);
            return true;
        }
    }

    /// <summary>
    /// A session closed (<c>SessionEnd</c>): removes it, unless an applied event is newer. A stamped end is remembered,
    /// so a late event from before it is ignored. Returns whether it was applied.
    /// </summary>
    public bool End(Guid memberId, string sessionId, DateTimeOffset? occurredAt)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate)
        {
            var key = (memberId, sessionId);
            _sessions.TryGetValue(key, out var existing);
            if (IsStale(key, existing, occurredAt))
            {
                return false;
            }

            if (occurredAt is { } endedAt)
            {
                _ended[key] = (endedAt, clock.GetUtcNow());
            }

            Remove(memberId, sessionId);
            return true;
        }
    }

    /// <summary>
    /// Whether a stamped event is older than the session's last applied one, or no newer than an end seen within the
    /// late-event window.
    /// </summary>
    private bool IsStale((Guid MemberId, string SessionId) key, Entry? existing, DateTimeOffset? occurredAt) =>
        occurredAt is { } at
        && ((existing?.OccurredAt is { } last && at < last)
            || (_ended.TryGetValue(key, out var ended) && at <= ended.OccurredAt
                && clock.GetUtcNow() - ended.SeenAt < prompts.Value.LateEventWindow));

    /// <summary>
    /// A session asks (D3, D6): it waits on the prompt, which is held for the dashboard when <paramref name="away"/>
    /// and the change has a card, and otherwise is the terminal's at once. Replaces any earlier prompt of the session.
    /// The request must already be within <see cref="PromptLimits"/>.
    /// </summary>
    public PromptCreated Ask(
        Guid memberId,
        string memberName,
        SessionPlacement? placement,
        PromptRequest request,
        bool away)
    {
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        var held = away && placement is not null;
        lock (_gate)
        {
            var key = (memberId, request.SessionId);
            _sessions.TryGetValue(key, out var existing);
            if (existing?.PromptId is { } earlier && _prompts[earlier].IsPending)
            {
                Settle(_prompts[earlier], PromptWaitState.Released, answer: null);
            }

            DropPrompt(existing);
            var promptId = Guid.CreateVersion7();
            _prompts[promptId] = new Prompt(key, request, held, now);
            var view = new SessionView(
                existing?.View.Id ?? Guid.CreateVersion7(),
                memberId,
                memberName,
                request.Machine.Trim(),
                request.Kind == PromptKind.Question ? SessionState.WaitingOnQuestion : SessionState.WaitingOnPermission,
                null,
                now,
                now)
            {
                Prompt = new SessionPrompt(request.Kind, request.Kind == PromptKind.Permission ? request.Tool : null),
                PromptDetail = new PromptDetail(
                    promptId,
                    held,
                    now,
                    request.Kind == PromptKind.Question && request.Questions is { } questions ? [.. questions.Select(PromptQuestionView.For)] : null),
            };
            // Never ignored: a prompt comes from a sync hook, so the terminal is waiting on it now. It moves the stamp
            // on, so a late event from before it can't clear it (second review #2), and a new prompt forgets an end.
            _ended.Remove(key);
            var stamp = request.OccurredAt is { } at && !(existing?.OccurredAt > at) ? at : existing?.OccurredAt;
            Put(key, new Entry(placement, view, existing?.Order ?? ++_sequence, promptId, stamp), existing);
            return new PromptCreated(promptId.ToString(), held);
        }
    }

    /// <summary>A held permission prompt's input preview, already cut to 2 KB, for those who may answer it.</summary>
    public PreviewOutcome SetPreview(Guid memberId, Guid promptId, string preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        lock (_gate)
        {
            if (!_prompts.TryGetValue(promptId, out var prompt) || prompt.Session.MemberId != memberId)
            {
                return PreviewOutcome.NotFound;
            }

            if (!prompt.IsPending || prompt.Kind != PromptKind.Permission)
            {
                return PreviewOutcome.NotHeld;
            }

            var entry = _sessions[prompt.Session];
            Put(prompt.Session, entry with { View = entry.View with { PromptDetail = entry.View.PromptDetail! with { Preview = preview } } });
            return PreviewOutcome.Set;
        }
    }

    /// <summary>
    /// Waits up to <see cref="PromptOptions.WaitTimeout"/> for the prompt to be answered or released, and says where it
    /// stands. Null when the store doesn't know it (a restart, or not this member's): 404, and the hook gives up.
    /// Each call counts as the hook still being there (<see cref="PromptOptions.WaitGrace"/>).
    /// </summary>
    public async Task<PromptWaitResult?> WaitAsync(Guid memberId, Guid promptId, CancellationToken cancellationToken)
    {
        Task settled;
        lock (_gate)
        {
            if (!_prompts.TryGetValue(promptId, out var prompt) || prompt.Session.MemberId != memberId)
            {
                return null;
            }

            if (!prompt.IsPending)
            {
                return Read(promptId, prompt);
            }

            prompt.LastWaitAt = clock.GetUtcNow();
            settled = prompt.Settled.Task;
        }

        try
        {
            await settled.WaitAsync(prompts.Value.WaitTimeout, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Still pending: the hook calls again.
        }

        lock (_gate)
        {
            if (!_prompts.TryGetValue(promptId, out var prompt))
            {
                return null;
            }

            if (prompt.IsPending)
            {
                prompt.LastWaitAt = clock.GetUtcNow();
                return new PromptWaitResult(prompt.State);
            }

            return Read(promptId, prompt);
        }
    }

    /// <summary>A settled prompt's result, which the hook now has: a detached one has done its job and goes.</summary>
    private PromptWaitResult Read(Guid promptId, Prompt prompt)
    {
        prompt.Read = true;
        if (prompt.Detached)
        {
            _prompts.Remove(promptId);
        }

        return new PromptWaitResult(prompt.State, prompt.Answer);
    }

    /// <summary>
    /// The hook gave up on a prompt it created before waiting (code review F3): a pending one goes back to the terminal,
    /// so no one answers it in vain. False when the store doesn't know it, or it isn't this member's (404). One being
    /// answered right now is left to land: the record stands, as when a session ends mid-answer.
    /// </summary>
    public bool Release(Guid memberId, Guid promptId)
    {
        lock (_gate)
        {
            if (!_prompts.TryGetValue(promptId, out var prompt) || prompt.Session.MemberId != memberId)
            {
                return false;
            }

            if (prompt.IsPending && !prompt.Claimed)
            {
                Settle(prompt, PromptWaitState.Released, answer: null);
            }

            return true;
        }
    }

    /// <summary>Gives every pending prompt of the member back to the terminal (they turned away off). Returns how many.</summary>
    public int ReleaseHeld(Guid memberId)
    {
        lock (_gate)
        {
            var pending = _prompts.Values.Where(p => p.Session.MemberId == memberId && p.IsPending && !p.Claimed).ToList();
            pending.ForEach(p => Settle(p, PromptWaitState.Released, answer: null));
            return pending.Count;
        }
    }

    /// <summary>
    /// What an answer needs to know about a prompt before it's claimed: who it belongs to and where, and what it asks.
    /// Null when the store doesn't know it. <see cref="PromptSnapshot.Answerable"/> is false once it's answered, being
    /// answered, released, or was never held.
    /// </summary>
    public PromptSnapshot? Find(Guid promptId)
    {
        lock (_gate)
        {
            // A detached answer is only the hook's to collect: to anyone answering, it's gone (404).
            if (!_prompts.TryGetValue(promptId, out var prompt) || prompt.Detached
                || !_sessions.TryGetValue(prompt.Session, out var entry))
            {
                return null;
            }

            var placement = entry.Placement;
            return new PromptSnapshot(
                promptId,
                prompt.Session.MemberId,
                placement,
                prompt.Kind,
                prompt.Questions,
                prompt.Tool,
                prompt.IsPending && !prompt.Claimed && placement is not null);
        }
    }

    /// <summary>
    /// The compare-and-set (D6; spec remote-answers, Two answers at once): true for exactly one caller while the prompt
    /// is pending and unclaimed. The winner then calls <see cref="CompleteAnswer"/> once its record is written, or
    /// <see cref="Unclaim"/> if that fails.
    /// </summary>
    public bool TryClaim(Guid promptId)
    {
        lock (_gate)
        {
            if (!_prompts.TryGetValue(promptId, out var prompt) || !prompt.IsPending || prompt.Claimed)
            {
                return false;
            }

            prompt.Claimed = true;
            return true;
        }
    }

    /// <summary>Gives a claimed prompt back, pending, after recording its answer failed.</summary>
    public void Unclaim(Guid promptId)
    {
        lock (_gate)
        {
            if (_prompts.TryGetValue(promptId, out var prompt))
            {
                prompt.Claimed = false;
            }
        }
    }

    /// <summary>
    /// Settles a claimed prompt with its answer, waking the hook, and sets the session back to working (D6: a denied
    /// tool gets no <c>PostToolUse</c>, so nothing else would).
    /// </summary>
    public void CompleteAnswer(Guid promptId, PromptAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        lock (_gate)
        {
            // The session ended while the answer was being recorded: the record stands, and there's no one to tell.
            if (!_prompts.TryGetValue(promptId, out var prompt) || !prompt.Claimed)
            {
                return;
            }

            prompt.Claimed = false;
            prompt.State = PromptWaitState.Answered;
            prompt.Answer = answer;
            prompt.AnsweredAt = clock.GetUtcNow();
            prompt.Settled.TrySetResult();
            if (_sessions.TryGetValue(prompt.Session, out var entry))
            {
                var now = clock.GetUtcNow();
                var working = entry.View with
                {
                    State = SessionState.Working,
                    Activity = null,
                    Since = now,
                    LastEventAt = now,
                    Prompt = null,
                    PromptDetail = null,
                };

                // The prompt stays known, answered, until the hook's wait reads it (DropPrompt keeps it past the
                // session's next event), so the answer always reaches the terminal.
                Put(prompt.Session, entry with { View = working });
            }
        }
    }

    /// <summary>Removes a session (<c>SessionEnd</c>); nothing happens for one the store doesn't hold.</summary>
    public void Remove(Guid memberId, string sessionId)
    {
        lock (_gate)
        {
            if (_sessions.Remove((memberId, sessionId), out var removed))
            {
                if (removed.PromptId is { } promptId && _prompts[promptId].IsPending)
                {
                    _prompts[promptId].Answer = null;
                    _prompts[promptId].State = PromptWaitState.Released;
                    _prompts[promptId].Settled.TrySetResult();
                }

                DropPrompt(removed);
                if (removed.Placement is { } placement)
                {
                    broadcaster.PublishSessionEnded(placement.ChangeId, placement.Scope, removed.View.Id);
                }
            }
        }
    }

    /// <summary>The sessions on a change, oldest first.</summary>
    public IReadOnlyList<SessionView> For(Guid changeId)
    {
        lock (_gate)
        {
            return [.. _sessions.Values
                .Where(e => e.Placement?.ChangeId == changeId)
                .OrderBy(e => e.Order)
                .Select(e => e.View)];
        }
    }

    /// <summary>The sessions on each of <paramref name="changeIds"/>, for a card list read in one pass.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<SessionView>> For(IReadOnlyCollection<Guid> changeIds)
    {
        ArgumentNullException.ThrowIfNull(changeIds);
        var wanted = changeIds.ToHashSet();
        lock (_gate)
        {
            return _sessions.Values
                .Where(e => e.Placement is { } p && wanted.Contains(p.ChangeId))
                .OrderBy(e => e.Order)
                .GroupBy(e => e.Placement!.ChangeId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SessionView>)[.. g.Select(e => e.View)]);
        }
    }

    /// <summary>How many sessions the store holds, placed or not.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count;
            }
        }
    }

    /// <summary>
    /// Releases held prompts that have waited <see cref="PromptOptions.HoldLimit"/> or had no <c>wait</c> for
    /// <see cref="PromptOptions.WaitGrace"/>, then drops the sessions with no event for the stale threshold
    /// (<see cref="ActivityOptions.StaleAfter"/>), as if each had ended, never one with a pending prompt. Returns how
    /// many sessions went.
    /// </summary>
    public int Sweep()
    {
        var now = clock.GetUtcNow();
        var limits = prompts.Value;
        var cutoff = now - activity.Value.StaleAfter;
        lock (_gate)
        {
            foreach (var prompt in _prompts.Values.Where(p => p.IsPending && !p.Claimed).ToList())
            {
                if (now - prompt.AskedAt >= limits.HoldLimit || now - prompt.LastWaitAt >= limits.WaitGrace)
                {
                    Settle(prompt, PromptWaitState.Released, answer: null);
                }
            }

            var expired = _sessions
                .Where(s => s.Value.View.LastEventAt <= cutoff && !(s.Value.PromptId is { } id && _prompts[id].IsPending))
                .ToList();
            foreach (var (key, _) in expired)
            {
                Remove(key.MemberId, key.SessionId);
            }

            // A detached answer the hook never came back for: it's gone too.
            foreach (var (id, _) in _prompts.Where(p => p.Value.Detached && now - p.Value.AnsweredAt >= limits.WaitGrace).ToList())
            {
                _prompts.Remove(id);
            }

            // Past the late-event window, an end no longer turns events away.
            foreach (var key in _ended.Where(e => now - e.Value.SeenAt >= limits.LateEventWindow).Select(e => e.Key).ToList())
            {
                _ended.Remove(key);
            }

            return expired.Count;
        }
    }

    /// <summary>
    /// Moves a pending prompt on, wakes its waits, and republishes its session: a released prompt is shown as the
    /// terminal's. The stale threshold counts from now (spec agent-presence, A held prompt keeps its session).
    /// </summary>
    private void Settle(Prompt prompt, PromptWaitState state, PromptAnswer? answer)
    {
        prompt.State = state;
        prompt.Answer = answer;
        prompt.Settled.TrySetResult();
        var entry = _sessions[prompt.Session];
        var detail = entry.View.PromptDetail! with { Held = false };
        Put(prompt.Session, entry with { View = entry.View with { PromptDetail = detail, LastEventAt = clock.GetUtcNow() } });
    }

    /// <summary>
    /// The session is done with its prompt. An answer no wait has read yet is kept, detached, for the hook to collect
    /// (code review F4); anything else goes.
    /// </summary>
    private void DropPrompt(Entry? entry)
    {
        if (entry?.PromptId is not { } promptId || !_prompts.TryGetValue(promptId, out var prompt))
        {
            return;
        }

        if (prompt.State == PromptWaitState.Answered && !prompt.Read)
        {
            prompt.Detached = true;
            return;
        }

        _prompts.Remove(promptId);
    }

    /// <summary>Stores the entry and publishes it, ending it on its old change first if it moved.</summary>
    private void Put((Guid MemberId, string SessionId) key, Entry entry, Entry? previous = null)
    {
        previous ??= _sessions.GetValueOrDefault(key);
        if (previous?.Placement is { } old && old != entry.Placement)
        {
            broadcaster.PublishSessionEnded(old.ChangeId, old.Scope, previous.View.Id);
        }

        _sessions[key] = entry;
        if (entry.Placement is { } placement)
        {
            broadcaster.PublishSessionUpdated(placement.ChangeId, placement.Scope, entry.View);
        }
    }
}

/// <summary>Runs <see cref="PresenceStore.Sweep"/> every <see cref="Interval"/>, often enough for the 90s wait grace.</summary>
public sealed partial class PresenceSweep(PresenceStore store, TimeProvider time, ILogger<PresenceSweep> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (store.Sweep() is > 0 and var swept)
            {
                LogSwept(swept);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Expired {Count} quiet agent sessions")]
    private partial void LogSwept(int count);
}
