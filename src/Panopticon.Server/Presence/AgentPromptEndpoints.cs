using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;

namespace Panopticon.Server.Presence;

/// <summary>
/// The sync hook's endpoints for a session's prompt (agent-interaction D3, D6), for reporters by bearer token. A prompt
/// is the token's member's: anyone else's token gets 404 for it, as for one the server doesn't know.
/// </summary>
public static class AgentPromptEndpoints
{
    public const string PromptsPath = "/" + AgentApiPaths.Prompts;

    public static string PreviewPath(Guid promptId) => $"{PromptsPath}/{promptId}/preview";

    public static string WaitPath(Guid promptId) => $"{PromptsPath}/{promptId}/wait";

    public static IEndpointRouteBuilder MapAgentPrompts(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PromptsPath, AskAsync).RequireAuthorization(AuthPolicies.Reporter);
        endpoints.MapPut($"{PromptsPath}/{{id:guid}}/preview", SetPreview).RequireAuthorization(AuthPolicies.Reporter);
        endpoints.MapGet($"{PromptsPath}/{{id:guid}}/wait", WaitAsync).RequireAuthorization(AuthPolicies.Reporter);
        endpoints.MapDelete($"{PromptsPath}/{{id:guid}}", Release).RequireAuthorization(AuthPolicies.Reporter);
        return endpoints;
    }

    public static string PromptPath(Guid promptId) => $"{PromptsPath}/{promptId}";

    /// <summary>
    /// The hook gave up on its prompt before waiting (code review F3): a pending one goes back to the terminal, and can't
    /// be answered from the dashboard. 204, also when it had already moved on; 404 unknown, or not this member's.
    /// </summary>
    internal static IResult Release(Guid id, ClaimsPrincipal user, PresenceStore presence) =>
        user.GetMemberId() is { } memberId && presence.Release(memberId, id)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    /// <summary>
    /// Records the prompt on its session and says whether it's held (D3: the server decides, from the member's away
    /// flag, in this one call). 400 for a malformed prompt or one over the limits: the hook checks those first and
    /// sends such a prompt to the terminal. 403 for a bootstrap token, which has no member to be away.
    /// </summary>
    internal static async Task<IResult> AskAsync(
        PromptRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        PresenceStore presence,
        CancellationToken cancellationToken)
    {
        if (AgentSessionEndpoints.SessionFieldsProblem(request.Repo, request.Change, request.Machine, request.SessionId) is { } fields)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, fields);
        }

        if (PromptLimits.Problem(request) is { } limits)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, $"This prompt can't go to the dashboard: {limits}.");
        }

        if (user.GetMemberId() is not { } memberId)
        {
            return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "This token isn't a member's, so its prompts stay in the terminal.");
        }

        var member = await db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => new { m.DisplayName, m.AwaySince })
            .SingleAsync(cancellationToken);
        var placement = await SessionChanges.FindAsync(db, memberId, request.Repo, request.Change, request.Project, cancellationToken);
        var created = presence.Ask(memberId, member.DisplayName, placement, request, away: member.AwaySince is not null);

        // Away turned off between the read and the hold released everything held before this prompt existed: look again.
        if (created.Held
            && await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.AwaySince).SingleAsync(cancellationToken) is null)
        {
            presence.ReleaseHeld(memberId);
            created = created with { Held = false };
        }

        return TypedResults.Ok(created);
    }

    /// <summary>A held permission prompt's input preview: 204, 400 over 2 KB, 404 unknown, 409 not held.</summary>
    internal static IResult SetPreview(Guid id, PromptPreview request, ClaimsPrincipal user, PresenceStore presence)
    {
        if (request.Preview is null || Encoding.UTF8.GetByteCount(request.Preview) > PromptLimits.MaxPermissionPreviewBytes)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "A preview is at most 2 KB.");
        }

        if (user.GetMemberId() is not { } memberId)
        {
            return TypedResults.NotFound();
        }

        return presence.SetPreview(memberId, id, request.Preview) switch
        {
            PreviewOutcome.Set => TypedResults.NoContent(),
            PreviewOutcome.NotHeld => SignInEndpoints.Problem(HttpStatusCode.Conflict, "Only a held permission prompt takes a preview."),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>The long-poll: where the prompt stands within about 25s, or 404 once the server doesn't know it.</summary>
    internal static async Task<IResult> WaitAsync(Guid id, ClaimsPrincipal user, PresenceStore presence, CancellationToken cancellationToken)
    {
        if (user.GetMemberId() is not { } memberId)
        {
            return TypedResults.NotFound();
        }

        return await presence.WaitAsync(memberId, id, cancellationToken) is { } result
            ? TypedResults.Ok(result)
            : TypedResults.NotFound();
    }
}
