using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Dashboard;

// projects D3; spec change-visibility: "Live updates are scoped" (Another Project's report, Removed while watching).
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

    private static async Task<string> NextUpdatedChangeAsync(StreamReader reader, CancellationToken cancellationToken) =>
        JsonDocument.Parse(await NextEventAsync(reader, DashboardEndpoints.ChangeUpdatedEvent, cancellationToken))
            .RootElement.GetProperty("change").GetString()!;
}
