using System.Net;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Live;

// spec tags: "Tag edits are live" (server side) and "Personal tags are private" on the live stream (design D6).
public class TagStreamTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChangeCard Card(string change) => new(
        Guid.NewGuid(),
        new CardRepo("github.com", "emmz", "panopticon"),
        change,
        [],
        null,
        new CardProgress(0, 1),
        DateTimeOffset.UnixEpoch);

    private static IAsyncEnumerator<SseItem<object?>> Stream(ChangeBroadcaster broadcaster, Viewer viewer) =>
        DashboardEndpoints.StreamAsync(broadcaster, viewer, subscribed: null, TimeSpan.FromSeconds(30), recheck: null, Ct).GetAsyncEnumerator(Ct);

    // The stream subscribes on its first MoveNextAsync, so a test starts that before publishing, then awaits it here.
    private static async Task<ChangeCard> NextCardAsync(IAsyncEnumerator<SseItem<object?>> stream, Task<bool> moving)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Assert.True(await moving.WaitAsync(timeout.Token));
        Assert.Equal(DashboardEndpoints.ChangeUpdatedEvent, stream.Current.EventType);
        return Assert.IsType<ChangeCard>(stream.Current.Data);
    }

    private static string Wire(ChangeCard card) => JsonSerializer.Serialize(card, JsonSerializerOptions.Web);

    private static List<string> Keys(ChangeCard card) => [.. card.PersonalTags.Select(t => t.Key)];

    [Fact]
    public async Task Each_subscriber_gets_only_their_own_personal_tags_on_a_shared_card()
    {
        var (emmz, sam, kit, pan) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var broadcaster = new ChangeBroadcaster();
        await using var emmzs = Stream(broadcaster, new Viewer(emmz, [pan]));
        await using var sams = Stream(broadcaster, new Viewer(sam, [pan]));
        await using var kits = Stream(broadcaster, new Viewer(kit, [pan]));
        var card = Card("pan-work");
        var (emmzMoving, samMoving, kitMoving) = (emmzs.MoveNextAsync().AsTask(), sams.MoveNextAsync().AsTask(), kits.MoveNextAsync().AsTask());

        broadcaster.Publish(new LiveCard(card, new Dictionary<Guid, IReadOnlyList<PersonalTagRef>>
        {
            [emmz] = [new PersonalTagRef("dev", null)],
            [sam] = [new PersonalTagRef("secret", null)],
        }), new ChangeScope(pan, null));

        var (toEmmz, toSam, toKit) = (await NextCardAsync(emmzs, emmzMoving), await NextCardAsync(sams, samMoving), await NextCardAsync(kits, kitMoving));
        Assert.Equal(["dev"], Keys(toEmmz));
        Assert.Equal(["secret"], Keys(toSam));
        Assert.Empty(toKit.PersonalTags);
        Assert.DoesNotContain("secret", Wire(toEmmz), StringComparison.Ordinal);
        Assert.DoesNotContain("dev", Wire(toSam), StringComparison.Ordinal);
        Assert.DoesNotContain("dev", Wire(toKit), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", Wire(toKit), StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_carrying_personal_tags_cant_be_published_to_everyone()
    {
        var broadcaster = new ChangeBroadcaster();
        var card = Card("pan-work") with { PersonalTags = [new PersonalTagRef("dev", null)] };

        Assert.Throws<ArgumentException>(() => broadcaster.Publish(card, ChangeScope.Bootstrap));
    }

    [Fact]
    public async Task A_report_sends_its_reporters_personal_tags_to_them_alone()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var broadcaster = world.Factory.Services.GetRequiredService<ChangeBroadcaster>();
        var emmz = await world.Factory.MemberIdAsync(cancellationToken: Ct);
        var sam = await world.Factory.MemberIdAsync(TagWorld.SamEmail, Ct);
        await using var emmzs = Stream(broadcaster, new Viewer(emmz, [world.Pan]));
        await using var sams = Stream(broadcaster, new Viewer(sam, [world.Pan]));
        var (emmzMoving, samMoving) = (emmzs.MoveNextAsync().AsTask(), sams.MoveNextAsync().AsTask());

        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["only-emmz"]), Ct);

        var toEmmz = await NextCardAsync(emmzs, emmzMoving);
        Assert.Equal(change, toEmmz.ChangeId);
        Assert.Equal(["only-emmz"], Keys(toEmmz));
        var toSam = await NextCardAsync(sams, samMoving);
        Assert.Equal(change, toSam.ChangeId);
        Assert.DoesNotContain("only-emmz", Wire(toSam), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tagging_a_change_live_keeps_each_members_personal_tags_their_own()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["only-emmz"]), Ct);
        var broadcaster = world.Factory.Services.GetRequiredService<ChangeBroadcaster>();
        var emmz = await world.Factory.MemberIdAsync(cancellationToken: Ct);
        var sam = await world.Factory.MemberIdAsync(TagWorld.SamEmail, Ct);
        await using var emmzs = Stream(broadcaster, new Viewer(emmz, [world.Pan]));
        await using var sams = Stream(broadcaster, new Viewer(sam, [world.Pan]));
        var (emmzMoving, samMoving) = (emmzs.MoveNextAsync().AsTask(), sams.MoveNextAsync().AsTask());

        using (var tagged = await world.Sam.PostApiAsync(ChangeTagEndpoints.TagsPath(change), new { add = new[] { bug } }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        }

        var toEmmz = await NextCardAsync(emmzs, emmzMoving);
        Assert.Equal(["bug"], toEmmz.ProjectTags.Select(t => t.Key));
        Assert.Equal(["only-emmz"], Keys(toEmmz));
        var toSam = await NextCardAsync(sams, samMoving);
        Assert.Equal(["bug"], toSam.ProjectTags.Select(t => t.Key));
        Assert.DoesNotContain("only-emmz", Wire(toSam), StringComparison.Ordinal);
    }
}
