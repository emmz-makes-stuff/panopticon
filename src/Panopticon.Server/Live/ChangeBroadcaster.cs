using System.Threading.Channels;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Presence;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Live;

/// <summary>
/// What the live stream carries (change-documents-and-history D10): an updated card, or an archived change. Each carries
/// its change's scope, so a subscription sends it only to a viewer who can see it (projects D3). It also carries the
/// membership changes that alter a member's scope, so their open streams follow at once (projects D3, §11).
/// </summary>
public abstract record ChangeEvent
{
    private ChangeEvent()
    {
    }

    /// <summary>
    /// A report or a tag edit moved an active change on; sent as <c>change-updated</c> with the card. <see cref="Live"/>
    /// holds the card with no personal tags and each member's beside it, so each subscriber gets only their own (tags
    /// D6). The map is never serialised.
    /// </summary>
    public sealed record Updated(LiveCard Live, ChangeScope Scope) : ChangeEvent
    {
        /// <summary>The card, with no personal tags on it.</summary>
        public ChangeCard Card => Live.Card;

        /// <summary>
        /// The card for one subscriber: with their own personal tags and nobody else's, and only the prompts they may
        /// answer in full (agent-interaction D5).
        /// </summary>
        public ChangeCard CardFor(Viewer viewer) => Live.For(viewer, Scope);
    }

    /// <summary>The change was archived and leaves the live view; sent as <c>change-archived</c>.</summary>
    public sealed record Archived(Guid ChangeId, ChangeScope Scope) : ChangeEvent;

    /// <summary>
    /// A member joined a Project, left it, or changed role there. Their streams update their scope and send
    /// <c>projects-changed</c>; nobody else's notice it. <see cref="Role"/> is their role now, or null once they've left,
    /// so a promoted or demoted owner's stream follows at once (agent-interaction D5).
    /// </summary>
    public sealed record MembershipChanged(Guid MemberId, Guid ProjectId, string? Role) : ChangeEvent
    {
        public bool IsMember => Role is not null;
    }

    /// <summary>A member was removed from the server: their streams end, so the browser's reconnect gets a 401.</summary>
    public sealed record MemberRemoved(Guid MemberId) : ChangeEvent;

    /// <summary>A Project's tags changed (tags D6); sent as <c>project-tags-changed</c> to the Project's members.</summary>
    public sealed record TagsChanged(Guid ProjectId, IReadOnlyList<ProjectTagRef> Tags) : ChangeEvent;

    /// <summary>A session on the change has a new state (agent-interaction D4); sent as <c>session-updated</c>.</summary>
    public sealed record SessionUpdated(Guid ChangeId, ChangeScope Scope, SessionView Session) : ChangeEvent;

    /// <summary>A session left the change, by ending, expiring or moving on; sent as <c>session-ended</c>.</summary>
    public sealed record SessionEnded(Guid ChangeId, ChangeScope Scope, Guid SessionId) : ChangeEvent;

    /// <summary>A member turned away on or off (D8); sent as <c>away-changed</c> to that member's streams only.</summary>
    public sealed record AwayChanged(Guid MemberId, Contracts.AwayState State) : ChangeEvent;

    /// <summary>
    /// An answer from the dashboard was recorded (D8); sent as <c>answer-recorded</c> for live change pages, with its
    /// questions only to viewers who could have answered (D5).
    /// </summary>
    public sealed record AnswerRecorded(ChangeScope Scope, Guid SessionMemberId, AnswerEntry Entry) : ChangeEvent;
}

/// <summary>
/// Fans change updates out to live subscribers, in process (design D9; one instance in this change).
/// Each subscriber has a bounded queue; a slow one drops its oldest updates rather than blocking reports.
/// </summary>
public sealed class ChangeBroadcaster
{
    public const int SubscriberCapacity = 64;

    private readonly Lock _gate = new();
    private readonly List<Channel<ChangeEvent>> _subscribers = [];

    // Changes announced as archived. A report that read its card before the archive committed can publish after the
    // archived event; that update is dropped, so it can't put the card back. An archived change's id never becomes
    // active again (a reused name gets a new change), and there's one id per archive since start-up.
    private readonly HashSet<Guid> _archived = [];

    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    public Subscription Subscribe()
    {
        var channel = Channel.CreateBounded<ChangeEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        return new Subscription(this, channel);
    }

    /// <summary>Publishes a card with each member's personal tags (tags D6).</summary>
    public void Publish(LiveCard card, ChangeScope scope)
    {
        ArgumentNullException.ThrowIfNull(card);
        Publish(card.Card, scope, card.PersonalTags);
    }

    /// <summary>Publishes a card with no personal tags on it.</summary>
    /// <exception cref="ArgumentException">The card carries personal tags: they would reach every subscriber.</exception>
    public void Publish(
        ChangeCard card,
        ChangeScope scope,
        IReadOnlyDictionary<Guid, IReadOnlyList<PersonalTagRef>>? personalTags = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(scope);
        if (card.PersonalTags.Count > 0)
        {
            throw new ArgumentException("A published card carries no personal tags: pass them by member instead (tags D6).", nameof(card));
        }

        Publish(new ChangeEvent.Updated(new LiveCard(card, personalTags ?? new Dictionary<Guid, IReadOnlyList<PersonalTagRef>>()), scope));
    }

    public void PublishArchived(Guid changeId, ChangeScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Publish(new ChangeEvent.Archived(changeId, scope));
    }

    /// <summary>
    /// Call after a membership write commits (invite, role change, removal, leaving, creating a Project), so the member's
    /// open streams take it in at once. Their keep-alive re-check reloads the Projects as a backstop, in case a full
    /// queue drops this.
    /// </summary>
    /// <param name="role">The member's role in the Project now, or null when they've left it.</param>
    public void PublishMembershipChanged(Guid memberId, Guid projectId, string? role) =>
        Publish(new ChangeEvent.MembershipChanged(memberId, projectId, role));

    /// <summary>Call after removing a member from the server commits: their open streams end.</summary>
    public void PublishMemberRemoved(Guid memberId) => Publish(new ChangeEvent.MemberRemoved(memberId));

    /// <summary>Call after a Project tag write commits, with the Project's tags as they are now (tags D6).</summary>
    public void PublishProjectTags(Guid projectId, IReadOnlyList<ProjectTagRef> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        Publish(new ChangeEvent.TagsChanged(projectId, tags));
    }

    /// <summary>Called by the <see cref="PresenceStore"/>, under its lock, so a session's events go out in order.</summary>
    public void PublishSessionUpdated(Guid changeId, ChangeScope scope, SessionView session)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(session);
        Publish(new ChangeEvent.SessionUpdated(changeId, scope, session));
    }

    /// <summary>Call after an answer's record commits.</summary>
    public void PublishAnswerRecorded(ChangeScope scope, Guid sessionMemberId, AnswerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(entry);
        Publish(new ChangeEvent.AnswerRecorded(scope, sessionMemberId, entry));
    }

    /// <summary>Call after a member's away switch is written.</summary>
    public void PublishAwayChanged(Guid memberId, Contracts.AwayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Publish(new ChangeEvent.AwayChanged(memberId, state));
    }

    public void PublishSessionEnded(Guid changeId, ChangeScope scope, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Publish(new ChangeEvent.SessionEnded(changeId, scope, sessionId));
    }

    private void Publish(ChangeEvent change)
    {
        lock (_gate)
        {
            if (change is ChangeEvent.Updated updated && _archived.Contains(updated.Card.ChangeId))
            {
                return;
            }

            if (change is ChangeEvent.Archived archived)
            {
                _archived.Add(archived.ChangeId);
            }

            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(change);
            }
        }
    }

    private void Remove(Channel<ChangeEvent> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    public sealed class Subscription(ChangeBroadcaster owner, Channel<ChangeEvent> channel) : IDisposable
    {
        public ChannelReader<ChangeEvent> Reader => channel.Reader;

        public void Dispose() => owner.Remove(channel);
    }
}
