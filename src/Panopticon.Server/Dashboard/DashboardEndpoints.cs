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

        endpoints.MapGet(EventsPath, (HttpResponse response, ChangeBroadcaster broadcaster, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(StreamAsync(broadcaster, token => SendHeadersAsync(response, token), cancellationToken)));

        return endpoints;
    }

    // StartAsync only fixes the headers; the flush puts them on the wire.
    private static async Task SendHeadersAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await response.StartAsync(cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Change updates for one connection; the subscription ends with the request. Once subscribed, <paramref name="subscribed"/>
    /// runs (the endpoint starts the response there), so a browser's <c>EventSource</c> opens at once instead of on the first
    /// event, and the refresh it does on open can't miss an update.
    /// </summary>
    internal static async IAsyncEnumerable<SseItem<ChangeCard>> StreamAsync(
        ChangeBroadcaster broadcaster,
        Func<CancellationToken, Task>? subscribed,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = broadcaster.Subscribe();
        if (subscribed is not null)
        {
            await subscribed(cancellationToken);
        }

        await foreach (var card in subscription.Reader.ReadAllAsync(cancellationToken))
        {
            yield return new SseItem<ChangeCard>(card, ChangeUpdatedEvent)
            {
                EventId = card.LastReportedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
