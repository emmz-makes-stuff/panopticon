using System.Diagnostics;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Presence;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Dashboard;

/// <summary>The data of a <c>change-archived</c> event.</summary>
public sealed record ArchivedChange(Guid ChangeId);

/// <summary>
/// The data of a <c>projects-changed</c> event: the viewer joined the Project (<paramref name="Member"/> true), left it,
/// or changed role there (true).
/// </summary>
public sealed record ProjectsChanged(Guid ProjectId, bool Member);

/// <summary>
/// The data of a <c>project-tags-changed</c> event: the Project's tags as they are now, after one was added, renamed or
/// deleted (tags D6). The page renames its cards' tags by id, and drops the ids that are gone.
/// </summary>
public sealed record ProjectTagsChanged(Guid ProjectId, IReadOnlyList<ProjectTagRef> Tags);

/// <summary>The data of a <c>session-updated</c> event: the session as it is now, on its change.</summary>
public sealed record SessionUpdatedData(Guid ChangeId, SessionView Session);

/// <summary>The data of a <c>session-ended</c> event: the session leaves the change's card.</summary>
public sealed record SessionEndedData(Guid ChangeId, Guid SessionId);

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

    /// <summary>
    /// The viewer's membership of a Project changed (projects D3, §11): the dashboard drops a left Project's cards, or
    /// refetches them for a joined one. Data is <see cref="ProjectsChanged"/>.
    /// </summary>
    public const string ProjectsChangedEvent = "projects-changed";

    /// <summary>A Project's tags changed (tags D6), sent to the Project's members. Data is <see cref="ProjectTagsChanged"/>.</summary>
    public const string ProjectTagsChangedEvent = "project-tags-changed";

    /// <summary>A session on a change has a new state (agent-interaction D4). Data is <see cref="SessionUpdatedData"/>.</summary>
    public const string SessionUpdatedEvent = "session-updated";

    /// <summary>A session left a change (ended, expired or moved on). Data is <see cref="SessionEndedData"/>.</summary>
    public const string SessionEndedEvent = "session-ended";

    /// <summary>An answer from the dashboard was recorded on a change (D8). Data is <see cref="AnswerEntry"/>.</summary>
    public const string AnswerRecordedEvent = "answer-recorded";

    /// <summary>The viewer turned away on or off (D8), from any dashboard or the CLI. Data is <see cref="Contracts.AwayState"/>.</summary>
    public const string AwayChangedEvent = "away-changed";

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
    /// viewer (team-auth D11) and returns them as they are now, with their Projects reloaded. Once it returns null (signed
    /// out, removed), the stream ends, and the browser's reconnect gets a 401.
    /// <para>
    /// Membership changes take effect at once (projects D3, §11): this member's <see cref="ChangeEvent.MembershipChanged"/>
    /// updates the viewer and sends <see cref="ProjectsChangedEvent"/>, and their <see cref="ChangeEvent.MemberRemoved"/>
    /// ends the stream. A re-check that finds the Projects changed sends the same events, in case a full queue dropped one.
    /// </para>
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

                        foreach (var difference in ProjectsChangedBetween(viewer, current))
                        {
                            yield return difference;
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

                foreach (var difference in ProjectsChangedBetween(viewer, current))
                {
                    lastSent = Stopwatch.GetTimestamp();
                    yield return difference;
                }

                viewer = current;
                lastCheck = Stopwatch.GetTimestamp();
            }

            while (reader.TryRead(out var change))
            {
                // projects D3, §11: this member's membership changes take effect at once, in order with the changes.
                if (change is ChangeEvent.MemberRemoved removed)
                {
                    if (removed.MemberId == viewer.MemberId)
                    {
                        yield break;
                    }

                    continue;
                }

                if (change is ChangeEvent.MembershipChanged membership)
                {
                    if (membership.MemberId == viewer.MemberId)
                    {
                        viewer = viewer.WithProject(membership.ProjectId, membership.Role);
                        lastSent = Stopwatch.GetTimestamp();
                        yield return ProjectsChangedItem(membership.ProjectId, membership.IsMember);
                    }

                    continue;
                }

                // agent-interaction D8: a member's away switch goes to their own streams only.
                if (change is ChangeEvent.AwayChanged awayChanged)
                {
                    if (awayChanged.MemberId == viewer.MemberId)
                    {
                        lastSent = Stopwatch.GetTimestamp();
                        yield return new SseItem<object?>(awayChanged.State, AwayChangedEvent);
                    }

                    continue;
                }

                // projects D3: only the changes this viewer can see.
                var (scope, item) = change switch
                {
                    // tags D6: the subscriber's own personal tags only, from the event's map; no database read.
                    ChangeEvent.Updated updated => (updated.Scope, new SseItem<object?>(updated.CardFor(viewer), ChangeUpdatedEvent)
                    {
                        EventId = updated.Card.LastReportedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    }),
                    ChangeEvent.Archived(var changeId, var archivedScope) =>
                        (archivedScope, new SseItem<object?>(new ArchivedChange(changeId), ChangeArchivedEvent)),
                    ChangeEvent.TagsChanged(var projectId, var tags) => (
                        new ChangeScope(projectId, null),
                        new SseItem<object?>(new ProjectTagsChanged(projectId, tags), ProjectTagsChangedEvent)),
                    ChangeEvent.SessionUpdated(var changeId, var sessionScope, var session) =>
                        (sessionScope, new SseItem<object?>(new SessionUpdatedData(changeId, session.For(viewer, sessionScope)), SessionUpdatedEvent)),
                    ChangeEvent.SessionEnded(var changeId, var sessionScope, var sessionId) =>
                        (sessionScope, new SseItem<object?>(new SessionEndedData(changeId, sessionId), SessionEndedEvent)),
                    ChangeEvent.AnswerRecorded(var answerScope, var sessionMemberId, var entry) =>
                        (answerScope, new SseItem<object?>(entry.For(viewer, sessionMemberId, answerScope), AnswerRecordedEvent)),
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

    /// <summary>
    /// The <c>projects-changed</c> events for the Projects a re-check found joined or left: the backstop for a
    /// membership event a full queue dropped.
    /// </summary>
    private static IEnumerable<SseItem<object?>> ProjectsChangedBetween(Viewer before, Viewer now) =>
        now.ProjectIds.Except(before.ProjectIds).Select(p => ProjectsChangedItem(p, isMember: true))
            .Concat(before.ProjectIds.Except(now.ProjectIds).Select(p => ProjectsChangedItem(p, isMember: false)));

    private static SseItem<object?> ProjectsChangedItem(Guid projectId, bool isMember) =>
        new(new ProjectsChanged(projectId, isMember), ProjectsChangedEvent);
}
