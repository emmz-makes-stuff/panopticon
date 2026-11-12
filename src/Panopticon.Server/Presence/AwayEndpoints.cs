using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Live;

namespace Panopticon.Server.Presence;

/// <summary>
/// A member's away switch (agent-interaction D8; spec remote-answers, Away mode): <c>Member.AwaySince</c> in the
/// database, so it survives a restart. Every change publishes <c>away-changed</c> to that member's streams only.
/// </summary>
public sealed class AwayMode(PanopticonDb db, ChangeBroadcaster broadcaster, PresenceStore presence, TimeProvider clock)
{
    public async Task<AwayState> GetAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var since = await db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => m.AwaySince)
            .SingleAsync(cancellationToken);
        return new AwayState(since is not null, since);
    }

    /// <summary>
    /// Turns away on or off. Turning it on again keeps the original time. Turning it off gives every held prompt
    /// back to the terminal (D6), after the write commits. Every call publishes the state, so a dashboard that
    /// missed one catches up on the next.
    /// </summary>
    public async Task<AwayState> SetAsync(Guid memberId, bool away, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (away)
        {
            await db.Members
                .Where(m => m.Id == memberId && m.AwaySince == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.AwaySince, now), cancellationToken);
        }
        else
        {
            await db.Members
                .Where(m => m.Id == memberId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.AwaySince, (DateTimeOffset?)null), cancellationToken);
        }

        var state = await GetAsync(memberId, cancellationToken);
        if (!state.Away)
        {
            presence.ReleaseHeld(memberId);
        }

        broadcaster.PublishAwayChanged(memberId, state);
        return state;
    }
}

/// <summary>
/// <c>GET</c> / <c>PUT /api/agent/away</c> for <c>panopticon away</c> (the reporter's token), and <c>/api/me/away</c>
/// for the dashboard (the session cookie). Both take and give <see cref="AwayState"/> (D3).
/// </summary>
public static class AwayEndpoints
{
    public const string AgentPath = "/" + AgentApiPaths.Away;
    public const string MePath = "/api/me/away";

    public static IEndpointRouteBuilder MapAway(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AgentPath, GetAsync).RequireAuthorization(AuthPolicies.Reporter);
        endpoints.MapPut(AgentPath, SetAsync).RequireAuthorization(AuthPolicies.Reporter);
        endpoints.MapGet(MePath, GetAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPut(MePath, SetAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    internal static async Task<IResult> GetAsync(ClaimsPrincipal user, AwayMode away, CancellationToken cancellationToken) =>
        user.GetMemberId() is { } memberId
            ? TypedResults.Ok(await away.GetAsync(memberId, cancellationToken))
            : NoMember();

    internal static async Task<IResult> SetAsync(AwayState request, ClaimsPrincipal user, AwayMode away, CancellationToken cancellationToken) =>
        user.GetMemberId() is { } memberId
            ? TypedResults.Ok(await away.SetAsync(memberId, request.Away, cancellationToken))
            : NoMember();

    // A bootstrap token reports for nobody, so there's no one to be away.
    private static IResult NoMember() =>
        SignInEndpoints.Problem(HttpStatusCode.Forbidden, "This token isn't a member's, so it has no away switch. Run `panopticon login`.");
}
