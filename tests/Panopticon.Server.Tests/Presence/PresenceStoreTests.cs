using Microsoft.Extensions.Options;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Presence;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D4; spec agent-presence: Session identity, Session states, Presence is not history.
public sealed class PresenceStoreTests : IDisposable
{
    private static readonly Guid _emmz = Guid.NewGuid();
    private static readonly Guid _sam = Guid.NewGuid();
    private static readonly SessionPlacement _teamAuth = new(Guid.NewGuid(), new ChangeScope(Guid.NewGuid(), null));
    private static readonly SessionPlacement _tags = new(Guid.NewGuid(), new ChangeScope(null, _emmz));

    private readonly ManualClock _clock = ManualClock.StartingAt2026();
    private readonly ChangeBroadcaster _broadcaster = new();
    private readonly ChangeBroadcaster.Subscription _subscription;
    private readonly PresenceStore _store;

    public PresenceStoreTests()
    {
        _subscription = _broadcaster.Subscribe();
        _store = new PresenceStore(_broadcaster, _clock, Options.Create(new ActivityOptions()), Options.Create(new PromptOptions()));
    }

    public void Dispose() => _subscription.Dispose();

    private List<ChangeEvent> Published()
    {
        var events = new List<ChangeEvent>();
        while (_subscription.Reader.TryRead(out var change))
        {
            events.Add(change);
        }

        return events;
    }

    private void Work(Guid member, string session, SessionPlacement? placement, string activity = "Editing tasks.md") =>
        _store.Record(member, member == _emmz ? "Emmz" : "Sam", session, placement, "vm-1", SessionState.Working, activity);

    [Fact]
    public void Two_sessions_on_one_change_each_keep_their_own_activity()
    {
        Work(_emmz, "s-1", _teamAuth, "Run make gates");
        _clock.Advance(TimeSpan.FromSeconds(5));
        Work(_emmz, "s-2", _teamAuth, "Editing design.md");

        var sessions = _store.For(_teamAuth.ChangeId);

        Assert.Equal(["Run make gates", "Editing design.md"], sessions.Select(s => s.Activity));
        Assert.NotEqual(sessions[0].Id, sessions[1].Id);
        Assert.All(sessions, s => Assert.Equal(("Emmz", "vm-1"), (s.MemberName, s.Machine)));
        var updates = Published().Cast<ChangeEvent.SessionUpdated>().ToList();
        Assert.Equal(2, updates.Count);
        Assert.All(updates, u => Assert.Equal((_teamAuth.ChangeId, _teamAuth.Scope), (u.ChangeId, u.Scope)));
    }

    [Fact]
    public void The_same_session_id_from_two_members_is_two_sessions()
    {
        Work(_emmz, "s-1", _teamAuth);
        Work(_sam, "s-1", _teamAuth);

        Assert.Equal(["Emmz", "Sam"], _store.For(_teamAuth.ChangeId).Select(s => s.MemberName));
    }

    [Fact]
    public void A_session_whose_change_has_no_card_is_kept_but_shows_nowhere()
    {
        Work(_emmz, "s-1", placement: null);

        Assert.Equal(1, _store.Count);
        Assert.Empty(_store.For(_teamAuth.ChangeId));
        Assert.Empty(_store.For([_teamAuth.ChangeId, _tags.ChangeId]));
        Assert.Empty(Published());

        _store.Remove(_emmz, "s-1");
        Assert.Equal(0, _store.Count);
        Assert.Empty(Published());
    }

    [Fact]
    public void Session_end_removes_the_session_and_publishes_its_end()
    {
        Work(_emmz, "s-1", _teamAuth);
        var id = _store.For(_teamAuth.ChangeId).Single().Id;
        Published();

        _store.Record(_emmz, "Emmz", "s-1", _teamAuth, "vm-1", SessionState.Ended, null);

        Assert.Empty(_store.For(_teamAuth.ChangeId));
        var ended = Assert.IsType<ChangeEvent.SessionEnded>(Assert.Single(Published()));
        Assert.Equal((_teamAuth.ChangeId, _teamAuth.Scope, id), (ended.ChangeId, ended.Scope, ended.SessionId));

        _store.Remove(_emmz, "s-1");
        Assert.Empty(Published());
    }

    [Fact]
    public void Waiting_drops_the_activity_and_restarts_the_clock_but_the_same_activity_keeps_it()
    {
        Work(_emmz, "s-1", _teamAuth, "Run make gates");
        var started = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromMinutes(2));
        Work(_emmz, "s-1", _teamAuth, "Run make gates");
        Assert.Equal(started, _store.For(_teamAuth.ChangeId).Single().Since);

        _clock.Advance(TimeSpan.FromMinutes(1));
        _store.Record(_emmz, "Emmz", "s-1", _teamAuth, "vm-1", SessionState.WaitingOnUser, "ignored");

        var session = _store.For(_teamAuth.ChangeId).Single();
        Assert.Equal((SessionState.WaitingOnUser, (string?)null), (session.State, session.Activity));
        Assert.Equal(_clock.GetUtcNow(), session.Since);
        Assert.Equal(_clock.GetUtcNow(), session.LastEventAt);
    }

    [Fact]
    public void Moving_to_another_change_ends_the_session_on_the_old_one()
    {
        Work(_emmz, "s-1", _teamAuth);
        var id = _store.For(_teamAuth.ChangeId).Single().Id;
        Published();

        Work(_emmz, "s-1", _tags);

        Assert.Empty(_store.For(_teamAuth.ChangeId));
        Assert.Equal(id, _store.For(_tags.ChangeId).Single().Id);
        var events = Published();
        Assert.Equal(_teamAuth.ChangeId, Assert.IsType<ChangeEvent.SessionEnded>(events[0]).ChangeId);
        Assert.Equal(_tags.ChangeId, Assert.IsType<ChangeEvent.SessionUpdated>(events[1]).ChangeId);
    }

    [Fact]
    public void A_session_expires_after_the_stale_threshold_with_no_event()
    {
        Work(_emmz, "s-1", _teamAuth);
        _clock.Advance(TimeSpan.FromHours(3));
        Work(_emmz, "s-2", _teamAuth);
        var newer = _store.For(_teamAuth.ChangeId)[1].Id;
        Published();

        _clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, _store.Sweep());

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _store.Sweep());

        Assert.Equal(newer, _store.For(_teamAuth.ChangeId).Single().Id);
        Assert.IsType<ChangeEvent.SessionEnded>(Assert.Single(Published()));

        _clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(1, _store.Sweep());
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Card_reads_get_each_changes_sessions_and_the_newest_event_moves_last_active()
    {
        Work(_emmz, "s-1", _teamAuth);
        Work(_emmz, "s-2", _tags);
        Work(_sam, "s-3", _tags);

        var byChange = _store.For([_teamAuth.ChangeId, _tags.ChangeId]);

        Assert.Single(byChange[_teamAuth.ChangeId]);
        Assert.Equal(["Emmz", "Sam"], byChange[_tags.ChangeId].Select(s => s.MemberName));

        var reported = _clock.GetUtcNow() - TimeSpan.FromHours(1);
        var card = new ChangeCard(_tags.ChangeId, new CardRepo("h", "o", "n"), "tags", [], null, new CardProgress(0, 1), reported);
        Assert.Equal(reported, card.LastActiveAt);
        Assert.Equal(_clock.GetUtcNow(), (card with { Sessions = byChange[_tags.ChangeId] }).LastActiveAt);
    }

    [Fact]
    public async Task A_wait_on_a_pending_prompt_answers_pending_once_the_clock_passes_its_timeout()
    {
        var clock = ManualClock.WithTimers();
        var options = new PromptOptions();
        var store = new PresenceStore(new ChangeBroadcaster(), clock, Options.Create(new ActivityOptions()), Options.Create(options));
        var request = new PromptRequest(new RepoRef("github.com", "emmz", "panopticon"), "team-auth", "vm-1", "s-1", PromptKind.Permission, Tool: "Bash");
        var created = store.Ask(_emmz, "Emmz", _teamAuth, request, away: true);

        var waiting = store.WaitAsync(_emmz, Guid.Parse(created.PromptId), TestContext.Current.CancellationToken);
        clock.Advance(options.WaitTimeout - TimeSpan.FromSeconds(1));
        Assert.False(waiting.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new PromptWaitResult(PromptWaitState.Pending), await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}
