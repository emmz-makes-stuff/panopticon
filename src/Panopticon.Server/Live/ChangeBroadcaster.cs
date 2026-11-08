using System.Threading.Channels;
using Panopticon.Server.Dashboard;

namespace Panopticon.Server.Live;

/// <summary>What the live stream carries (change-documents-and-history D10): an updated card, or an archived change.</summary>
public abstract record ChangeEvent
{
    private ChangeEvent()
    {
    }

    /// <summary>A report moved an active change on; sent as <c>change-updated</c> with the card.</summary>
    public sealed record Updated(ChangeCard Card) : ChangeEvent;

    /// <summary>The change was archived and leaves the live view; sent as <c>change-archived</c>.</summary>
    public sealed record Archived(Guid ChangeId) : ChangeEvent;
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

    public void Publish(ChangeCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Publish(new ChangeEvent.Updated(card));
    }

    public void PublishArchived(Guid changeId) => Publish(new ChangeEvent.Archived(changeId));

    private void Publish(ChangeEvent change)
    {
        lock (_gate)
        {
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
