using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;

namespace Panopticon.Server.Tests.Live;

public class ChangeBroadcasterTests
{
    private static ChangeCard Card(int done) => new(
        Guid.NewGuid(),
        new CardRepo("github.com", "emmz", "panopticon"),
        "walking-skeleton",
        [],
        null,
        new CardProgress(done, 100),
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void Every_subscriber_receives_a_published_card()
    {
        var broadcaster = new ChangeBroadcaster();
        using var first = broadcaster.Subscribe();
        using var second = broadcaster.Subscribe();

        broadcaster.Publish(Card(1));

        Assert.True(first.Reader.TryRead(out var a));
        Assert.True(second.Reader.TryRead(out var b));
        Assert.Same(a, b);
    }

    [Fact]
    public void A_slow_subscriber_drops_its_oldest_updates()
    {
        var broadcaster = new ChangeBroadcaster();
        using var slow = broadcaster.Subscribe();

        for (var i = 0; i < ChangeBroadcaster.SubscriberCapacity + 5; i++)
        {
            broadcaster.Publish(Card(i));
        }

        Assert.True(slow.Reader.TryRead(out var oldestKept));
        Assert.Equal(5, Assert.IsType<ChangeEvent.Updated>(oldestKept).Card.Overall.Done);
    }

    [Fact]
    public void An_archived_change_reaches_every_subscriber()
    {
        var broadcaster = new ChangeBroadcaster();
        using var first = broadcaster.Subscribe();
        using var second = broadcaster.Subscribe();
        var changeId = Guid.NewGuid();

        broadcaster.PublishArchived(changeId);

        Assert.True(first.Reader.TryRead(out var a));
        Assert.True(second.Reader.TryRead(out var b));
        Assert.Equal(new ChangeEvent.Archived(changeId), a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Disposing_unsubscribes_and_completes_the_reader()
    {
        var broadcaster = new ChangeBroadcaster();
        var subscription = broadcaster.Subscribe();

        subscription.Dispose();
        broadcaster.Publish(Card(1));

        Assert.Equal(0, broadcaster.SubscriberCount);
        Assert.True(subscription.Reader.Completion.IsCompleted);
    }
}
