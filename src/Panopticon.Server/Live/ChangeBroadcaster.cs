using System.Threading.Channels;
using Panopticon.Server.Dashboard;
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

    /// <summary>A report moved an active change on; sent as <c>change-updated</c> with the card.</summary>
    public sealed record Updated(ChangeCard Card, ChangeScope Scope) : ChangeEvent;

    /// <summary>The change was archived and leaves the live view; sent as <c>change-archived</c>.</summary>
    public sealed record Archived(Guid ChangeId, ChangeScope Scope) : ChangeEvent;

    /// <summary>
    /// A member joined a Project, left it, or changed role there. Their streams update their scope and send
    /// <c>projects-changed</c>; nobody else's notice it.
    /// </summary>
    public sealed record MembershipChanged(Guid MemberId, Guid ProjectId, bool IsMember) : ChangeEvent;

    /// <summary>A member was removed from the server: their streams end, so the browser's reconnect gets a 401.</summary>
    public sealed record MemberRemoved(Guid MemberId) : ChangeEvent;
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

    public void Publish(ChangeCard card, ChangeScope scope)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(scope);
        Publish(new ChangeEvent.Updated(card, scope));
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
    public void PublishMembershipChanged(Guid memberId, Guid projectId, bool isMember) =>
        Publish(new ChangeEvent.MembershipChanged(memberId, projectId, isMember));

    /// <summary>Call after removing a member from the server commits: their open streams end.</summary>
    public void PublishMemberRemoved(Guid memberId) => Publish(new ChangeEvent.MemberRemoved(memberId));

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
