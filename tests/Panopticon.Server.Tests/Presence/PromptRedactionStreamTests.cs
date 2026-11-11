using System.Net.ServerSentEvents;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Presence;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Tests.Presence;

// agent-interaction D5; spec remote-answers, What others see of a prompt. A prompt goes in full only to those who may
// answer it, and an open stream follows a promotion or demotion at once.
public class PromptRedactionStreamTests
{
    private static readonly Guid _emmz = Guid.NewGuid();
    private static readonly Guid _bob = Guid.NewGuid();
    private static readonly Guid _sam = Guid.NewGuid();
    private static readonly Guid _pan = Guid.NewGuid();
    private static readonly ChangeScope _panScope = new(_pan, null);
    private static readonly Guid _changeId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SessionView HeldPermission(string activityTag = "") => new(
        Guid.NewGuid(),
        _emmz,
        "Emmz",
        "vm-1",
        SessionState.WaitingOnPermission,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch)
    {
        Prompt = new SessionPrompt(PromptKind.Permission, "Bash"),
        PromptDetail = new PromptDetail(Guid.NewGuid(), Held: true, DateTimeOffset.UnixEpoch, Preview: $"curl -H \"Authorization: Bearer abc\" {activityTag}"),
    };

    private static ChangeCard CardWith(SessionView session) =>
        new ChangeCard(_changeId, new CardRepo("github.com", "emmz", "panopticon"), "team-auth", [], null, new CardProgress(0, 1), DateTimeOffset.UnixEpoch)
        {
            Sessions = [session],
        };

    private static IAsyncEnumerator<SseItem<object?>> Stream(ChangeBroadcaster broadcaster, Viewer viewer) =>
        DashboardEndpoints.StreamAsync(broadcaster, viewer, subscribed: null, TimeSpan.FromMinutes(10), recheck: null, Ct).GetAsyncEnumerator(Ct);

    private static async Task<T> NextAsync<T>(IAsyncEnumerator<SseItem<object?>> stream)
    {
        while (await stream.MoveNextAsync())
        {
            if (stream.Current.Data is T data)
            {
                return data;
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }

    [Fact]
    public async Task Bob_demoted_while_a_prompt_is_held_gets_his_next_event_redacted()
    {
        var broadcaster = new ChangeBroadcaster();
        await using var bob = Stream(broadcaster, new Viewer(_bob, [_pan], [_pan]));
        var moving = bob.MoveNextAsync();

        broadcaster.PublishSessionUpdated(_changeId, _panScope, HeldPermission("first"));
        Assert.True(await moving);
        var full = Assert.IsType<SessionUpdatedData>(bob.Current.Data).Session;
        Assert.Contains("first", full.PromptDetail!.Preview, StringComparison.Ordinal);

        broadcaster.PublishMembershipChanged(_bob, _pan, ProjectRoles.Editor);
        broadcaster.PublishSessionUpdated(_changeId, _panScope, HeldPermission("second"));
        broadcaster.Publish(CardWith(HeldPermission("third")), _panScope);

        Assert.Equal(new ProjectsChanged(_pan, true), await NextAsync<ProjectsChanged>(bob));
        var redacted = (await NextAsync<SessionUpdatedData>(bob)).Session;
        Assert.Null(redacted.PromptDetail);
        Assert.Equal(new SessionPrompt(PromptKind.Permission, "Bash"), redacted.Prompt);
        Assert.Equal(("Emmz", SessionState.WaitingOnPermission), (redacted.MemberName, redacted.State));
        var card = await NextAsync<ChangeCard>(bob);
        Assert.Null(Assert.Single(card.Sessions).PromptDetail);

        // And promoted again: in full once more.
        broadcaster.PublishMembershipChanged(_bob, _pan, ProjectRoles.Owner);
        broadcaster.PublishSessionUpdated(_changeId, _panScope, HeldPermission("fourth"));
        Assert.Contains("fourth", (await NextAsync<SessionUpdatedData>(bob)).Session.PromptDetail!.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_member_gets_it_in_full_and_an_editor_never_does()
    {
        var broadcaster = new ChangeBroadcaster();
        await using var emmz = Stream(broadcaster, new Viewer(_emmz, [_pan]));
        await using var sam = Stream(broadcaster, new Viewer(_sam, [_pan]));
        var emmzMoving = emmz.MoveNextAsync();
        var samMoving = sam.MoveNextAsync();

        broadcaster.PublishSessionUpdated(_changeId, _panScope, HeldPermission());
        broadcaster.Publish(CardWith(HeldPermission()), _panScope);

        Assert.True(await emmzMoving);
        Assert.NotNull(Assert.IsType<SessionUpdatedData>(emmz.Current.Data).Session.PromptDetail);
        Assert.NotNull(Assert.Single((await NextAsync<ChangeCard>(emmz)).Sessions).PromptDetail);

        Assert.True(await samMoving);
        Assert.Null(Assert.IsType<SessionUpdatedData>(sam.Current.Data).Session.PromptDetail);
        Assert.Null(Assert.Single((await NextAsync<ChangeCard>(sam)).Sessions).PromptDetail);
    }

    [Fact]
    public void A_card_read_redacts_per_viewer()
    {
        var card = CardWith(HeldPermission());

        Assert.NotNull(card.SessionsFor(new Viewer(_bob, [_pan], [_pan]), _panScope).Sessions[0].PromptDetail);
        Assert.Null(card.SessionsFor(new Viewer(_sam, [_pan]), _panScope).Sessions[0].PromptDetail);
        Assert.Null(card.SessionsFor(new Viewer(_bob, [_pan], [_pan]), new ChangeScope(null, _emmz)).Sessions[0].PromptDetail);
    }
}
