using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>The signed-in member, and signing out (spec dashboard-sign-in, "Signing out"; design D8).</summary>
public static class SessionEndpoints
{
    public const string SessionPath = "/api/session";
    public const string SignOutPath = "/api/auth/sign-out";

    public static IEndpointRouteBuilder MapSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SessionPath, GetAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPost(SignOutPath, SignOutAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        PanopticonDb db,
        InitialMemberList initialMembers,
        CancellationToken cancellationToken)
    {
        var memberId = user.GetMemberId();
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        return TypedResults.Ok(SessionMember.From(member, initialMembers.Contains(member.EmailKey)));
    }

    private static async Task<IResult> SignOutAsync(
        ClaimsPrincipal user,
        HttpContext context,
        SessionStore sessions,
        CancellationToken cancellationToken)
    {
        await sessions.EndAsync(user.GetSessionId()!.Value, context.Response, cancellationToken);
        return TypedResults.NoContent();
    }
}

/// <summary>
/// Cross-site protection (spec dashboard-sign-in, "Cross-site requests refused"; design D6). Every <c>/api</c> request
/// that isn't a GET must carry <c>X-Panopticon-Request: 1</c>. A cross-site form can't set a header, and a cross-site
/// fetch can't without a CORS preflight, which the server never allows. With the Lax session cookie, another site
/// can't make a member's browser change anything.
/// </summary>
public sealed class RequestHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Panopticon-Request";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method)
            && request.Headers[HeaderName] != "1")
        {
            await TypedResults.Problem(
                    detail: "This request must come from the Panopticon dashboard or CLI.",
                    statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
