using System.Net;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Changes;

/// <param name="Add">Ids of the Project's tags to put on the change.</param>
/// <param name="Remove">Ids of tags to take off it. A tag in both lists ends up on it.</param>
public sealed record ChangeTagsRequest(IReadOnlyList<Guid>? Add, IReadOnlyList<Guid>? Remove);

/// <summary>
/// Tagging a change from its page (spec tags, "Tagging a change on the dashboard"; design D4). Active and archived
/// changes alike; owners and editors of the change's Project only.
/// </summary>
public static class ChangeTagEndpoints
{
    public static string TagsPath(Guid changeId) => $"{DashboardEndpoints.ChangesPath}/{changeId}/tags";

    public static IEndpointRouteBuilder MapChangeTags(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost($"{DashboardEndpoints.ChangesPath}/{{id:guid}}/tags", TagAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    /// <summary>Applies the request and answers with the change's card as the caller sees it; an active one goes out live too.</summary>
    internal static async Task<IResult> TagAsync(
        Guid id,
        ChangeTagsRequest request,
        Viewer viewer,
        PanopticonDb db,
        ChangeCards cards,
        ChangeBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        var change = await db.Changes.AsNoTracking().VisibleTo(viewer)
            .Where(c => c.Id == id)
            .Select(c => new { c.ProjectId, c.OwnerMemberId })
            .SingleOrDefaultAsync(cancellationToken);
        if (change is null)
        {
            return TypedResults.NotFound();
        }

        if (change.ProjectId is not { } projectId)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "Only a Project's changes have project tags.");
        }

        var (add, remove) = (request.Add ?? [], request.Remove ?? []);
        if (add.Count == 0 && remove.Count == 0)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "Say which tags to add or remove.");
        }

        switch (await ChangeTagging.ApplyAsync(db, id, projectId, viewer.MemberId, add, remove, cancellationToken))
        {
            case TaggingOutcome.NotAllowed:
                return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "Only the Project's owners and editors can tag its changes.");
            case TaggingOutcome.UnknownTag:
                return SignInEndpoints.Problem(
                    HttpStatusCode.BadRequest,
                    "One of those tags isn't one of this Project's tags. It may just have been deleted.");
        }

        var (card, archived) = await cards.GetIncludingArchivedAsync(id, cancellationToken);
        if (!archived)
        {
            broadcaster.Publish(card, new ChangeScope(projectId, change.OwnerMemberId));
        }

        return TypedResults.Ok(card.For(viewer.MemberId));
    }
}
