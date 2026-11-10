using System.Diagnostics;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Dashboard;

/// <summary>The data of a <c>change-archived</c> event.</summary>
public sealed record ArchivedChange(Guid ChangeId);

/// <summary>The dashboard's read API and live stream (spec: live-dashboard), for signed-in members only (dashboard-sign-in).</summary>
public static class DashboardEndpoints
{
    public const string ChangesPath = "/api/changes";

    /// <summary>The thresholds the web app works out quiet cards with (filters-and-idle-cards D1).</summary>
    public const string SettingsPath = "/api/settings";
    public const string EventsPath = "/api/events";
    public const string ChangeUpdatedEvent = "change-updated";

    /// <summary>A change was archived: its card leaves (change-documents-and-history D10). Data is <see cref="ArchivedChange"/>.</summary>
    public const string ChangeArchivedEvent = "change-archived";

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

        endpoints.MapGet(SettingsPath, (IOptions<ActivityOptions> activity) => TypedResults.Ok(DashboardSettings.From(activity.Value)))
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(ChangesPath, async (Viewer viewer, ChangeCards cards, CancellationToken cancellationToken) =>
            TypedResults.Ok(await cards.ListAsync(viewer, cancellationToken)))
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(
            EventsPath,
            (
                HttpResponse response,
                ClaimsPrincipal user,
                Viewer viewer,
                SessionStore sessions,
                PanopticonDb db,
                ChangeBroadcaster broadcaster,
                CancellationToken cancellationToken) =>
            {
                var sessionId = user.GetSessionId()!.Value;
                return TypedResults.ServerSentEvents(StreamAsync(
                    broadcaster,
                    viewer,
                    token => SendHeadersAsync(response, token),
                    keepAlive,
                    async token => await sessions.IsLiveAsync(sessionId, token)
                        ? await Viewer.LoadAsync(db, viewer.MemberId, token)
                        : null,
                    cancellationToken));
            })
            .RequireAuthorization(AuthPolicies.Member);

        return endpoints;
    }

    // StartAsync only fixes the headers; the flush puts them on the wire.
    private static async Task SendHeadersAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await response.StartAsync(cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Change updates for one connection, only those <paramref name="viewer"/> can see (projects D3); the subscription
    /// ends with the request. Once subscribed, <paramref name="subscribed"/> runs (the endpoint starts the response
    /// there), so a browser's <c>EventSource</c> opens at once instead of on the first event, and the refresh it does on
    /// open can't miss an update. Whenever nothing has been sent for <paramref name="keepAlive"/>, a
    /// <see cref="PingEvent"/> (with null data) goes instead. About as often, <paramref name="recheck"/> re-checks the
    /// viewer (team-auth D11) and returns them as they are now, with their Projects reloaded, so joining or leaving one
    /// takes effect within a keep-alive. Once it returns null (signed out, removed), the stream ends, and the browser's
    /// reconnect gets a 401.
    /// </summary>
    internal static async IAsyncEnumerable<SseItem<object?>> StreamAsync(
        ChangeBroadcaster broadcaster,
        Viewer viewer,
        Func<CancellationToken, Task>? subscribed,
        TimeSpan keepAlive,
        Func<CancellationToken, Task<Viewer?>>? recheck,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = broadcaster.Subscribe();
        if (subscribed is not null)
        {
            await subscribed(cancellationToken);
        }

        var reader = subscription.Reader;
        var lastCheck = Stopwatch.GetTimestamp();
        var lastSent = lastCheck;
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
                    if (recheck is not null)
                    {
                        if (await recheck(cancellationToken) is not { } current)
                        {
                            yield break;
                        }

                        viewer = current;
                    }

                    lastCheck = lastSent = Stopwatch.GetTimestamp();
                    yield return new SseItem<object?>(null, PingEvent);
                    continue;
                }

                await idle.CancelAsync();
            }

            if (!await waiting)
            {
                yield break;
            }

            // A busy stream never goes quiet, so it re-checks on the same schedule before sending.
            if (recheck is not null && Stopwatch.GetElapsedTime(lastCheck) >= keepAlive)
            {
                if (await recheck(cancellationToken) is not { } current)
                {
                    yield break;
                }

                viewer = current;
                lastCheck = Stopwatch.GetTimestamp();
            }

            while (reader.TryRead(out var change))
            {
                // projects D3: only the changes this viewer can see.
                var (scope, item) = change switch
                {
                    ChangeEvent.Updated(var card, var cardScope) => (cardScope, new SseItem<object?>(card, ChangeUpdatedEvent)
                    {
                        EventId = card.LastReportedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    }),
                    ChangeEvent.Archived(var changeId, var archivedScope) =>
                        (archivedScope, new SseItem<object?>(new ArchivedChange(changeId), ChangeArchivedEvent)),
                    _ => throw new UnreachableException(),
                };
                if (viewer.CanSee(scope))
                {
                    lastSent = Stopwatch.GetTimestamp();
                    yield return item;
                }
            }

            // Busy with other scopes' changes, the stream would send this viewer nothing and never go quiet: ping.
            if (Stopwatch.GetElapsedTime(lastSent) >= keepAlive)
            {
                lastSent = Stopwatch.GetTimestamp();
                yield return new SseItem<object?>(null, PingEvent);
            }

            waiting = reader.WaitToReadAsync(cancellationToken).AsTask();
        }
    }
}
