using Microsoft.Extensions.Options;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
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
    DateTimeOffset LastEventAt);

/// <summary>
/// The sessions working on changes right now (D4; spec agent-presence: Presence is not history). In memory only: a
/// restart loses them, and nothing here touches the database. Sessions are keyed by the reporting member and Claude
/// Code's session id, so two members' ids can never collide. Every change that a dashboard can see is published on the
/// <see cref="ChangeBroadcaster"/>, under the store's lock, so one session's events go out in order.
/// </summary>
public sealed class PresenceStore(ChangeBroadcaster broadcaster, TimeProvider clock, IOptions<ActivityOptions> options)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid MemberId, string SessionId), Entry> _sessions = [];

    private long _sequence;

    // Order: when the store first saw the session, as a counter, so two sessions seen in the same instant keep theirs.
    private sealed record Entry(SessionPlacement? Placement, SessionView View, long Order);

    /// <summary>
    /// Records a session's new state. <paramref name="placement"/> is null when its change has no active card: the
    /// session is kept, but shows nowhere. <see cref="SessionState.Ended"/> removes it. Moving to another change ends
    /// it on the old one. Activity is kept only while working.
    /// </summary>
    public void Record(
        Guid memberId,
        string memberName,
        string sessionId,
        SessionPlacement? placement,
        string machine,
        SessionState state,
        string? activity)
    {
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(machine);
        if (state == SessionState.Ended)
        {
            Remove(memberId, sessionId);
            return;
        }

        var now = clock.GetUtcNow();
        activity = state == SessionState.Working ? activity : null;
        lock (_gate)
        {
            var key = (memberId, sessionId);
            _sessions.TryGetValue(key, out var existing);
            if (existing?.Placement is { } old && old != placement)
            {
                broadcaster.PublishSessionEnded(old.ChangeId, old.Scope, existing.View.Id);
            }

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
            _sessions[key] = new Entry(placement, view, existing?.Order ?? ++_sequence);
            if (placement is not null)
            {
                broadcaster.PublishSessionUpdated(placement.ChangeId, placement.Scope, view);
            }
        }
    }

    /// <summary>Removes a session (<c>SessionEnd</c>); nothing happens for one the store doesn't hold.</summary>
    public void Remove(Guid memberId, string sessionId)
    {
        lock (_gate)
        {
            if (_sessions.Remove((memberId, sessionId), out var removed) && removed.Placement is { } placement)
            {
                broadcaster.PublishSessionEnded(placement.ChangeId, placement.Scope, removed.View.Id);
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
    /// Drops the sessions with no event for the stale threshold (<see cref="ActivityOptions.StaleAfter"/>), as if
    /// each had ended. Returns how many went.
    /// </summary>
    public int Sweep()
    {
        var cutoff = clock.GetUtcNow() - options.Value.StaleAfter;
        lock (_gate)
        {
            var expired = _sessions.Where(s => s.Value.View.LastEventAt <= cutoff).ToList();
            foreach (var (key, entry) in expired)
            {
                _sessions.Remove(key);
                if (entry.Placement is { } placement)
                {
                    broadcaster.PublishSessionEnded(placement.ChangeId, placement.Scope, entry.View.Id);
                }
            }

            return expired.Count;
        }
    }
}

/// <summary>Runs <see cref="PresenceStore.Sweep"/> every <see cref="Interval"/>.</summary>
public sealed partial class PresenceSweep(PresenceStore store, TimeProvider time, ILogger<PresenceSweep> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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
