using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Dashboard;

// projects D3; spec change-visibility: "Live updates are scoped" and "Membership changes take effect live".
public class ScopedStreamTests(PostgresFixture postgres, S3Fixture s3)
{
    private static readonly (Guid Kit, Guid Emmz, Guid Pan, Guid Acme) _ids = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChangeCard Card(string change) => new(
        Guid.NewGuid(),
        new CardRepo("github.com", "emmz", "panopticon"),
        change,
        [],
        null,
        new CardProgress(0, 1),
        DateTimeOffset.UnixEpoch);

    // Code review F8: a dropped session-ended or away-changed would otherwise never be caught up.
    [Fact]
    public async Task A_stream_whose_queue_dropped_events_sends_resync_then_the_events_it_kept()
    {
        var broadcaster = new ChangeBroadcaster();
        var kit = new Viewer(_ids.Kit, [_ids.Pan]);
        var pan = new ChangeScope(_ids.Pan, null);
        await using var stream = DashboardEndpoints.StreamAsync(broadcaster, kit, subscribed: null, TimeSpan.FromSeconds(30), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();
        broadcaster.Publish(Card("first"), pan);
        Assert.True(await moving);

        // The stream is busy sending the first event while the queue overflows by one.
        var cards = Enumerable.Range(0, ChangeBroadcaster.SubscriberCapacity + 1).Select(i => Card($"card-{i}")).ToList();
        cards.ForEach(card => broadcaster.Publish(card, pan));

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal((DashboardEndpoints.ResyncEvent, null), (stream.Current.EventType, stream.Current.Data));
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("card-1", Assert.IsType<ChangeCard>(stream.Current.Data).Change);
    }

    // Second review #5: a dropped membership change takes effect at the resync, not at the next keep-alive.
    [Fact]
    public async Task A_resync_rechecks_the_viewer_at_once_so_a_dropped_join_takes_effect()
    {
        var broadcaster = new ChangeBroadcaster();
        var current = new Viewer(_ids.Kit, [_ids.Pan]);
        var pan = new ChangeScope(_ids.Pan, null);
        await using var stream = DashboardEndpoints.StreamAsync(
                broadcaster, current, subscribed: null, TimeSpan.FromMinutes(5), recheck: _ => Task.FromResult<Viewer?>(current), Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();
        broadcaster.Publish(Card("first"), pan);
        Assert.True(await moving);

        // Kit joined Acme; that event is among those the full queue dropped.
        current = new Viewer(_ids.Kit, [_ids.Pan, _ids.Acme]);
        Enumerable.Range(0, ChangeBroadcaster.SubscriberCapacity + 1).ToList().ForEach(i => broadcaster.Publish(Card($"card-{i}"), pan));

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(new ProjectsChanged(_ids.Acme, Member: true), stream.Current.Data);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(DashboardEndpoints.ResyncEvent, stream.Current.EventType);
    }

    [Fact]
    public async Task A_resync_whose_recheck_finds_the_member_removed_ends_the_stream()
    {
        var broadcaster = new ChangeBroadcaster();
        Viewer? current = new Viewer(_ids.Kit, [_ids.Pan]);
        var pan = new ChangeScope(_ids.Pan, null);
        await using var stream = DashboardEndpoints.StreamAsync(
                broadcaster, current, subscribed: null, TimeSpan.FromMinutes(5), recheck: _ => Task.FromResult<Viewer?>(current), Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();
        broadcaster.Publish(Card("first"), pan);
        Assert.True(await moving);

        current = null;
        Enumerable.Range(0, ChangeBroadcaster.SubscriberCapacity + 1).ToList().ForEach(i => broadcaster.Publish(Card($"card-{i}"), pan));

        Assert.False(await stream.MoveNextAsync());
    }

    [Fact]
    public async Task A_stream_with_no_drops_sends_no_resync()
    {
        var broadcaster = new ChangeBroadcaster();
        var kit = new Viewer(_ids.Kit, [_ids.Pan]);
        await using var stream = DashboardEndpoints.StreamAsync(broadcaster, kit, subscribed: null, TimeSpan.FromSeconds(30), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();
        broadcaster.Publish(Card("first"), new ChangeScope(_ids.Pan, null));
        broadcaster.Publish(Card("second"), new ChangeScope(_ids.Pan, null));

        Assert.True(await moving);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(DashboardEndpoints.ChangeUpdatedEvent, stream.Current.EventType);
        Assert.Equal("second", Assert.IsType<ChangeCard>(stream.Current.Data).Change);
    }

    [Fact]
    public async Task A_viewer_gets_only_the_events_of_changes_they_can_see()
    {
        var broadcaster = new ChangeBroadcaster();
        var kit = new Viewer(_ids.Kit, [_ids.Pan]);
        await using var stream = DashboardEndpoints.StreamAsync(broadcaster, kit, subscribed: null, TimeSpan.FromSeconds(30), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();

        var acme = Card("acme-work");
        broadcaster.Publish(acme, new ChangeScope(_ids.Acme, null));
        broadcaster.PublishArchived(Guid.NewGuid(), new ChangeScope(_ids.Acme, null));
        broadcaster.Publish(Card("emmz-scratch"), new ChangeScope(null, _ids.Emmz));
        var pan = Card("pan-work");
        broadcaster.Publish(pan, new ChangeScope(_ids.Pan, null));
        var kits = Card("kit-scratch");
        broadcaster.Publish(kits, new ChangeScope(null, _ids.Kit));
        var archivedPan = Guid.NewGuid();
        broadcaster.PublishArchived(archivedPan, new ChangeScope(_ids.Pan, null));
        var bootstrap = Card("walking-skeleton");
        broadcaster.Publish(bootstrap, ChangeScope.Bootstrap);

        var received = new List<object?>();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(i == 0 ? await moving : await stream.MoveNextAsync());
            received.Add(stream.Current.Data);
        }

        Assert.Equal([pan, kits, new ArchivedChange(archivedPan), bootstrap], received);
    }

    [Fact]
    public async Task A_viewer_removed_from_a_Project_stops_getting_its_events_after_a_keep_alive()
    {
        var broadcaster = new ChangeBroadcaster();
        var current = new Viewer(_ids.Kit, [_ids.Pan]);
        await using var stream = DashboardEndpoints.StreamAsync(
                broadcaster,
                current,
                subscribed: null,
                TimeSpan.FromMilliseconds(50),
                recheck: _ => Task.FromResult<Viewer?>(current),
                Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();
        var before = Card("pan-before");
        broadcaster.Publish(before, new ChangeScope(_ids.Pan, null));
        Assert.True(await moving);
        Assert.Equal(before, stream.Current.Data);

        current = new Viewer(_ids.Kit, []);
        await NextPingAsync(stream);
        broadcaster.Publish(Card("pan-after"), new ChangeScope(_ids.Pan, null));
        var sentinel = Card("kit-scratch");
        broadcaster.Publish(sentinel, new ChangeScope(null, _ids.Kit));

        Assert.Equal(sentinel, await NextDataAsync(stream));
    }

    // The same through the endpoint: its keep-alive re-check reloads the member's Projects from the database.
    [Fact]
    public async Task Removed_from_a_Project_while_watching_the_stream_stops_sending_its_updates()
    {
        const string kitEmail = "kit@example.com";
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Settings = new Dictionary<string, string> { [DashboardEndpoints.KeepAliveConfigKey] = "00:00:00.100" },
        };
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        await emmz.InviteToProjectAsync(pan, kitEmail, "viewer", Ct);
        using var kit = await factory.CreateSignedInClientAsync(kitEmail, Ct);
        var kitToken = await factory.CreateApiTokenAsync(kitEmail, cancellationToken: Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await kit.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

        await ReportAsync(factory, null, "pan-before", pan);
        Assert.Equal("pan-before", await NextUpdatedChangeAsync(reader, timeout.Token));

        var kitId = await factory.MemberIdAsync(kitEmail, Ct);
        (await emmz.SendApiAsync(HttpMethod.Delete, $"{ProjectEndpoints.MembersPath(pan)}/{kitId}", null, Ct)).Dispose();

        // Two pings after the removal: the re-check before the second ran after it.
        await NextEventAsync(reader, "ping", timeout.Token);
        await NextEventAsync(reader, "ping", timeout.Token);
        await ReportAsync(factory, null, "pan-after", pan);
        await ReportAsync(factory, kitToken, "kit-scratch", project: null);

        Assert.Equal("kit-scratch", await NextUpdatedChangeAsync(reader, timeout.Token));
    }

    // spec change-visibility, "Membership changes take effect live" (Removed while watching); projects D3, §11.
    [Fact]
    public async Task A_viewer_removed_from_a_Project_gets_projects_changed_and_no_later_update_of_it()
    {
        var broadcaster = new ChangeBroadcaster();
        await using var stream = DashboardEndpoints.StreamAsync(
                broadcaster,
                new Viewer(_ids.Kit, [_ids.Pan]),
                subscribed: null,
                TimeSpan.FromMinutes(10),
                recheck: null,
                Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();

        broadcaster.PublishMembershipChanged(_ids.Emmz, _ids.Pan, role: null);
        broadcaster.PublishMembershipChanged(_ids.Kit, _ids.Pan, role: null);
        broadcaster.Publish(Card("pan-after"), new ChangeScope(_ids.Pan, null));
        var sentinel = Card("kit-scratch");
        broadcaster.Publish(sentinel, new ChangeScope(null, _ids.Kit));

        Assert.True(await moving);
        Assert.Equal(DashboardEndpoints.ProjectsChangedEvent, stream.Current.EventType);
        Assert.Equal(new ProjectsChanged(_ids.Pan, Member: false), stream.Current.Data);
        Assert.Equal(sentinel, await NextDataAsync(stream));
    }

    // spec change-visibility, "Membership changes take effect live" (Invited while watching).
    [Fact]
    public async Task A_viewer_invited_to_a_Project_gets_projects_changed_then_its_updates()
    {
        var broadcaster = new ChangeBroadcaster();
        await using var stream = DashboardEndpoints.StreamAsync(
                broadcaster,
                new Viewer(_ids.Kit, []),
                subscribed: null,
                TimeSpan.FromMinutes(10),
                recheck: null,
                Ct)
            .GetAsyncEnumerator(Ct);
        var moving = stream.MoveNextAsync();

        broadcaster.Publish(Card("pan-before"), new ChangeScope(_ids.Pan, null));
        broadcaster.PublishMembershipChanged(_ids.Kit, _ids.Pan, ProjectRoles.Viewer);
        var after = Card("pan-after");
        broadcaster.Publish(after, new ChangeScope(_ids.Pan, null));

        Assert.True(await moving);
        Assert.Equal(new ProjectsChanged(_ids.Pan, Member: true), stream.Current.Data);
        Assert.Equal(after, await NextDataAsync(stream));
    }

    [Fact]
    public async Task Removing_a_member_from_the_server_ends_only_their_streams()
    {
        var broadcaster = new ChangeBroadcaster();
        await using var kits = DashboardEndpoints.StreamAsync(
                broadcaster, new Viewer(_ids.Kit, [_ids.Pan]), subscribed: null, TimeSpan.FromMinutes(10), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        await using var emmzs = DashboardEndpoints.StreamAsync(
                broadcaster, new Viewer(_ids.Emmz, [_ids.Pan]), subscribed: null, TimeSpan.FromMinutes(10), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        var kitMoving = kits.MoveNextAsync();
        var emmzMoving = emmzs.MoveNextAsync();

        broadcaster.PublishMemberRemoved(_ids.Kit);
        var pan = Card("pan-work");
        broadcaster.Publish(pan, new ChangeScope(_ids.Pan, null));

        Assert.False(await kitMoving);
        Assert.True(await emmzMoving);
        Assert.Equal(pan, emmzs.Current.Data);
    }

    // The backstop: a re-check that finds the Projects changed sends the events a full queue might have dropped.
    [Fact]
    public async Task A_re_check_that_finds_the_Projects_changed_sends_projects_changed_for_each()
    {
        var broadcaster = new ChangeBroadcaster();
        var current = new Viewer(_ids.Kit, [_ids.Pan]);
        await using var stream = DashboardEndpoints.StreamAsync(
                broadcaster,
                current,
                subscribed: null,
                TimeSpan.FromMilliseconds(50),
                recheck: _ => Task.FromResult<Viewer?>(current),
                Ct)
            .GetAsyncEnumerator(Ct);
        current = new Viewer(_ids.Kit, [_ids.Acme]);

        var received = new List<object?>();
        while (received.Count < 2 && await stream.MoveNextAsync())
        {
            if (stream.Current.EventType == DashboardEndpoints.ProjectsChangedEvent)
            {
                received.Add(stream.Current.Data);
            }
        }

        Assert.Equal([new ProjectsChanged(_ids.Acme, Member: true), new ProjectsChanged(_ids.Pan, Member: false)], received);
    }

    // The same through the endpoints, with a keep-alive too long to help: each membership write tells the stream.
    [Fact]
    public async Task Membership_changes_reach_an_open_stream_at_once()
    {
        const string kitEmail = "kit@example.com";
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Settings = new Dictionary<string, string> { [DashboardEndpoints.KeepAliveConfigKey] = "00:10:00" },
        };
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);

        // Kit needs a Project to be able to sign in at all.
        await emmz.InviteToProjectAsync(await emmz.CreateProjectAsync("OTH", Ct), kitEmail, "viewer", Ct);
        using var kit = await factory.CreateSignedInClientAsync(kitEmail, Ct);
        var kitId = await factory.MemberIdAsync(kitEmail, Ct);
        var kitToken = await factory.CreateApiTokenAsync(kitEmail, cancellationToken: Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await kit.GetAsync(DashboardEndpoints.EventsPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

        // Invited while watching: the event, then the Project's updates.
        await emmz.InviteToProjectAsync(pan, kitEmail, "viewer", Ct);
        Assert.Equal((pan, true), await NextProjectsChangedAsync(reader, timeout.Token));
        await ReportAsync(factory, null, "pan-joined", pan);
        Assert.Equal("pan-joined", await NextUpdatedChangeAsync(reader, timeout.Token));

        // A role change: still a member.
        (await emmz.SendApiAsync(HttpMethod.Patch, $"{ProjectEndpoints.MembersPath(pan)}/{kitId}", new ChangeRoleRequest("editor"), Ct))
            .Dispose();
        Assert.Equal((pan, true), await NextProjectsChangedAsync(reader, timeout.Token));

        // Creating a Project: the creator is in it.
        var kits = await kit.CreateProjectAsync("KIT", Ct);
        Assert.Equal((kits, true), await NextProjectsChangedAsync(reader, timeout.Token));

        // Removed while watching: the event, and no later update of the Project.
        (await emmz.SendApiAsync(HttpMethod.Delete, $"{ProjectEndpoints.MembersPath(pan)}/{kitId}", null, Ct)).Dispose();
        Assert.Equal((pan, false), await NextProjectsChangedAsync(reader, timeout.Token));
        await ReportAsync(factory, null, "pan-after", pan);
        await ReportAsync(factory, kitToken, "kit-scratch", project: null);
        Assert.Equal("kit-scratch", await NextUpdatedChangeAsync(reader, timeout.Token));

        // Removed from the server: the stream ends. Kit owns KIT alone, so Emmz is made a co-owner first (that event is
        // Emmz's, not Kit's).
        await kit.InviteToProjectAsync(kits, PanopticonFactory.MemberEmail, "owner", Ct);
        using (var removed = await emmz.SendApiAsync(HttpMethod.Delete, $"{MemberEndpoints.MembersPath}/{kitId}", null, Ct))
        {
            Assert.Equal(System.Net.HttpStatusCode.NoContent, removed.StatusCode);
        }

        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            Assert.DoesNotContain(DashboardEndpoints.ProjectsChangedEvent, line, StringComparison.Ordinal);
        }
    }

    private static async Task<(Guid ProjectId, bool Member)> NextProjectsChangedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var data = JsonDocument.Parse(await NextEventAsync(reader, DashboardEndpoints.ProjectsChangedEvent, cancellationToken)).RootElement;
        return (data.GetProperty("projectId").GetGuid(), data.GetProperty("member").GetBoolean());
    }

    private static async Task NextPingAsync(IAsyncEnumerator<SseItem<object?>> stream)
    {
        while (await stream.MoveNextAsync())
        {
            if (stream.Current.EventType == DashboardEndpoints.PingEvent)
            {
                return;
            }
        }

        Assert.Fail("The stream ended.");
    }

    private static async Task<object?> NextDataAsync(IAsyncEnumerator<SseItem<object?>> stream)
    {
        while (await stream.MoveNextAsync())
        {
            if (stream.Current.EventType != DashboardEndpoints.PingEvent)
            {
                return stream.Current.Data;
            }
        }

        Assert.Fail("The stream ended.");
        return null;
    }

    private static async Task ReportAsync(PanopticonFactory factory, string? token, string change, Guid? project)
    {
        await using var client = token is null ? await factory.CreateMcpClientAsync(Ct) : await factory.CreateMcpClientAsync(token, Ct);
        var report = new ReportProgressArguments(
            new RepoRef("github.com", "emmz", "panopticon"), change, new AgentRef("claude-code", "vm-1"), "- [ ] a\n")
        {
            Project = project?.ToString(),
        };
        var result = await client.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct);
        Assert.True(result.IsError is not true);
    }

    /// <summary>Reads SSE lines up to the next event of the type, returning its data line.</summary>
    private static async Task<string> NextEventAsync(StreamReader reader, string type, CancellationToken cancellationToken)
    {
        string? eventType = null;
        string? data = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (eventType == type)
                {
                    return data ?? string.Empty;
                }

                (eventType, data) = (null, null);
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line["data: ".Length..];
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }

    // tags D6; spec live-dashboard "Tag edits are live": a Project's tag changes reach its members, and nobody else.
    [Fact]
    public async Task Project_tag_changes_reach_only_that_Projects_members()
    {
        var broadcaster = new ChangeBroadcaster();
        var kit = new Viewer(_ids.Kit, [_ids.Acme]);
        var emmz = new Viewer(_ids.Emmz, [_ids.Pan]);
        await using var kits = DashboardEndpoints.StreamAsync(broadcaster, kit, subscribed: null, TimeSpan.FromSeconds(30), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        await using var emmzs = DashboardEndpoints.StreamAsync(broadcaster, emmz, subscribed: null, TimeSpan.FromSeconds(30), recheck: null, Ct)
            .GetAsyncEnumerator(Ct);
        var kitMoving = kits.MoveNextAsync();
        var emmzMoving = emmzs.MoveNextAsync();

        IReadOnlyList<ProjectTagRef> panTags = [new(Guid.NewGuid(), "defect", null)];
        broadcaster.PublishProjectTags(_ids.Pan, panTags);
        var acme = Card("acme-work");
        broadcaster.Publish(acme, new ChangeScope(_ids.Acme, null));

        Assert.True(await emmzMoving);
        Assert.Equal(DashboardEndpoints.ProjectTagsChangedEvent, emmzs.Current.EventType);
        Assert.Equal(new ProjectTagsChanged(_ids.Pan, panTags), emmzs.Current.Data);
        Assert.True(await kitMoving);
        Assert.Equal(acme, kits.Current.Data);
    }

    private static async Task<string> NextUpdatedChangeAsync(StreamReader reader, CancellationToken cancellationToken) =>
        JsonDocument.Parse(await NextEventAsync(reader, DashboardEndpoints.ChangeUpdatedEvent, cancellationToken))
            .RootElement.GetProperty("change").GetString()!;
}
