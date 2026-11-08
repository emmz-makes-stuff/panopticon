using System.Threading.Channels;
using Panopticon.Server.Dashboard;

namespace Panopticon.Server.Live;

/// <summary>
/// Fans change updates out to live subscribers, in process (design D9; one instance in this change).
/// Each subscriber has a bounded queue; a slow one drops its oldest updates rather than blocking reports.
/// </summary>
public sealed class ChangeBroadcaster
{
    public const int SubscriberCapacity = 64;

    private readonly Lock _gate = new();
    private readonly List<Channel<ChangeCard>> _subscribers = [];

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
        var channel = Channel.CreateBounded<ChangeCard>(new BoundedChannelOptions(SubscriberCapacity)
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
        lock (_gate)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(card);
            }
        }
    }

    private void Remove(Channel<ChangeCard> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    public sealed class Subscription(ChangeBroadcaster owner, Channel<ChangeCard> channel) : IDisposable
    {
        public ChannelReader<ChangeCard> Reader => channel.Reader;

        public void Dispose() => owner.Remove(channel);
    }
}
