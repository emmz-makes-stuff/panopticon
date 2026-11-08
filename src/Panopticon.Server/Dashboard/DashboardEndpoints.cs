using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Panopticon.Server.Live;

namespace Panopticon.Server.Dashboard;

/// <summary>The dashboard's read API and live stream (spec: live-dashboard). Unauthenticated in this change.</summary>
public static class DashboardEndpoints
{
    public const string ChangesPath = "/api/changes";
    public const string EventsPath = "/api/events";
    public const string ChangeUpdatedEvent = "change-updated";

    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ChangesPath, async (ChangeCards cards, CancellationToken cancellationToken) =>
            TypedResults.Ok(await cards.ListAsync(cancellationToken)));

        endpoints.MapGet(EventsPath, (ChangeBroadcaster broadcaster, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(StreamAsync(broadcaster, cancellationToken)));

        return endpoints;
    }

    /// <summary>Change updates for one connection; the subscription ends with the request.</summary>
    internal static async IAsyncEnumerable<SseItem<ChangeCard>> StreamAsync(
        ChangeBroadcaster broadcaster,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = broadcaster.Subscribe();
        await foreach (var card in subscription.Reader.ReadAllAsync(cancellationToken))
        {
            yield return new SseItem<ChangeCard>(card, ChangeUpdatedEvent)
            {
                EventId = card.LastReportedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
