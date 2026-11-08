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

    /// <summary>Sent on an idle stream so proxies and browsers don't drop it; the dashboard doesn't listen for it.</summary>
    public const string PingEvent = "ping";

    /// <summary>How long the stream may be quiet before a <see cref="PingEvent"/>; a TimeSpan setting.</summary>
    public const string KeepAliveConfigKey = "Dashboard:KeepAlive";

    public static readonly TimeSpan DefaultKeepAlive = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        var keepAlive = endpoints.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<TimeSpan?>(KeepAliveConfigKey)
            ?? DefaultKeepAlive;
        if (keepAlive <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{KeepAliveConfigKey} must be a positive TimeSpan.");
        }

        endpoints.MapGet(ChangesPath, async (ChangeCards cards, CancellationToken cancellationToken) =>
            TypedResults.Ok(await cards.ListAsync(cancellationToken)));

        endpoints.MapGet(EventsPath, (HttpResponse response, ChangeBroadcaster broadcaster, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(
                StreamAsync(broadcaster, token => SendHeadersAsync(response, token), keepAlive, cancellationToken)));

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
    /// event, and the refresh it does on open can't miss an update. Whenever nothing has been sent for
    /// <paramref name="keepAlive"/>, a <see cref="PingEvent"/> (with null data) goes instead.
    /// </summary>
    internal static async IAsyncEnumerable<SseItem<ChangeCard?>> StreamAsync(
        ChangeBroadcaster broadcaster,
        Func<CancellationToken, Task>? subscribed,
        TimeSpan keepAlive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = broadcaster.Subscribe();
        if (subscribed is not null)
        {
            await subscribed(cancellationToken);
        }

        var reader = subscription.Reader;
        var waiting = reader.WaitToReadAsync(cancellationToken).AsTask();
        while (true)
        {
            if (!waiting.IsCompleted)
            {
                // The same wait carries over a ping, so no card is lost to the race.
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var quiet = Task.Delay(keepAlive, idle.Token);
                if (await Task.WhenAny(waiting, quiet) == quiet)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new SseItem<ChangeCard?>(null, PingEvent);
                    continue;
                }

                await idle.CancelAsync();
            }

            if (!await waiting)
            {
                yield break;
            }

            while (reader.TryRead(out var card))
            {
                yield return new SseItem<ChangeCard?>(card, ChangeUpdatedEvent)
                {
                    EventId = card.LastReportedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                };
            }

            waiting = reader.WaitToReadAsync(cancellationToken).AsTask();
        }
    }
}
