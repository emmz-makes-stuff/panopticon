using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>One of your API tokens, as the tokens page lists it. The secret is never part of it: only its hash is kept.</summary>
public sealed record TokenView(Guid Id, string MachineName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>Your API tokens: list and revoke (spec reporter-credentials, "Tokens page"; design D13).</summary>
public static class TokenEndpoints
{
    public const string TokensPath = "/api/tokens";

    public static IEndpointRouteBuilder MapTokens(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(TokensPath, ListAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapDelete($"{TokensPath}/{{id:guid}}", RevokeAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    internal static async Task<IResult> ListAsync(ClaimsPrincipal user, PanopticonDb db, CancellationToken cancellationToken)
    {
        var memberId = user.GetMemberId();
        var tokens = await db.ApiTokens.AsNoTracking()
            .Where(t => t.MemberId == memberId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TokenView(t.Id, t.MachineName, t.CreatedAt, t.LastUsedAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(tokens);
    }

    /// <summary>Revokes one of your own tokens. Anyone else's, like an unknown or revoked one, is a 404.</summary>
    internal static async Task<IResult> RevokeAsync(
        Guid id,
        ClaimsPrincipal user,
        PanopticonDb db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var memberId = user.GetMemberId();
        var now = time.GetUtcNow();
        var revoked = await db.ApiTokens
            .Where(t => t.Id == id && t.MemberId == memberId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), cancellationToken);
        return revoked == 0
            ? SignInEndpoints.Problem(HttpStatusCode.NotFound, "There's no such token.")
            : TypedResults.NoContent();
    }
}
