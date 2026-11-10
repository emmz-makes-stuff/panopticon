using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Live;

namespace Panopticon.Server.Auth;

public sealed record RenameRequest(string? DisplayName);

/// <summary>An active member, as the members page lists them. <c>JoinedAt</c> is when the row was created.</summary>
public sealed record MemberView(Guid Id, string Email, string DisplayName, DateTimeOffset JoinedAt)
{
    public static MemberView From(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new(member.Id, member.Email, member.DisplayName, member.CreatedAt);
    }
}

/// <summary>
/// Listing, renaming and removing members (spec team-members; design D11, projects D8). People join through a
/// Project's invite now. Every endpoint needs a session; the state-changing ones also need the D6 header, which
/// <see cref="RequestHeaderMiddleware"/> enforces for all of <c>/api</c>.
/// </summary>
public static class MemberEndpoints
{
    public const string MembersPath = "/api/members";
    public const string MePath = "/api/members/me";

    public static IEndpointRouteBuilder MapMembers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(MembersPath, ListAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPatch(MePath, RenameAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapDelete($"{MembersPath}/{{id:guid}}", RemoveAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    /// <summary>
    /// Removes another member from the server (spec team-members, "Removing a member"); only an initial member may. In
    /// one transaction it sets <c>removed_at</c>, deletes their sessions, revokes their API tokens, deletes their unused
    /// sign-in links (so a link sent before removal can't sign them in if they're invited back), and takes them out of
    /// every Project. It's refused while they're the last owner of a Project, naming it (projects D8). Their open
    /// dashboard streams end on the next keep-alive check (design D11). The row stays, so their past reports keep their
    /// name.
    /// </summary>
    internal static async Task<IResult> RemoveAsync(
        Guid id,
        ClaimsPrincipal user,
        PanopticonDb db,
        InitialMemberList initialMembers,
        TimeProvider time,
        ChangeBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        var remover = user.GetRequiredMemberId();
        if (id == remover)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "You can't remove yourself. Ask another member to.");
        }

        if (!await CanRemoveMembersAsync(db, initialMembers, remover, cancellationToken))
        {
            return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "Only an initial member can remove members from the server.");
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Lock every active member row (in id order, so two removals can't deadlock). Two members removing each other at
        // once then take turns: the second sees that its remover has gone, so the team never ends up with nobody.
        var active = await db.Members
            .FromSql($"SELECT * FROM members WHERE removed_at IS NULL ORDER BY id FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (!active.Any(m => m.Id == id))
        {
            return SignInEndpoints.Problem(HttpStatusCode.NotFound, "There's no such member.");
        }

        if (!active.Any(m => m.Id == remover))
        {
            // Removed by someone else a moment ago. Since the remover is active otherwise, at least they remain.
            return SignInEndpoints.Problem(HttpStatusCode.Conflict, "You've been removed, so you can't remove anyone.");
        }

        // projects D8: never leave a Project without an owner. The Projects they own are locked, as the Project
        // endpoints lock them, so a role change can't slip in between the check and the delete.
        var owned = await db.Projects
            .FromSql($"""
                SELECT * FROM projects
                WHERE id IN (SELECT project_id FROM project_members WHERE member_id = {id} AND role = {ProjectRoles.Owner})
                ORDER BY id
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var ownedIds = owned.Select(p => p.Id).ToList();
        var coOwned = await db.ProjectMembers
            .Where(m => ownedIds.Contains(m.ProjectId) && m.MemberId != id && m.Role == ProjectRoles.Owner)
            .Select(m => m.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var lastOwnerOf = owned.Where(p => !coOwned.Contains(p.Id)).Select(p => p.Code).Order(StringComparer.Ordinal).ToList();
        if (lastOwnerOf.Count > 0)
        {
            return SignInEndpoints.Problem(
                HttpStatusCode.Conflict,
                $"They're the only owner of {string.Join(", ", lastOwnerOf)}. Make another member an owner there first.");
        }

        await db.Members
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, now), cancellationToken);

        await db.Sessions.Where(s => s.MemberId == id).ExecuteDeleteAsync(cancellationToken);
        await db.SignInLinks.Where(l => l.MemberId == id && l.UsedAt == null).ExecuteDeleteAsync(cancellationToken);
        await db.ApiTokens
            .Where(t => t.MemberId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), cancellationToken);
        await db.ProjectMembers.Where(m => m.MemberId == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // projects D3, §11: their open streams end now, not at the next keep-alive.
        broadcaster.PublishMemberRemoved(id);
        return TypedResults.NoContent();
    }

    /// <summary>Whether the member is an initial member, so may remove others (projects D8).</summary>
    internal static async Task<bool> CanRemoveMembersAsync(
        PanopticonDb db,
        InitialMemberList initialMembers,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var emailKey = await db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => m.EmailKey)
            .SingleAsync(cancellationToken);
        return initialMembers.Contains(emailKey);
    }

    /// <summary>
    /// The active members who share a Project with the caller, and the caller (spec team-members, "Member list";
    /// projects D8).
    /// </summary>
    internal static async Task<IResult> ListAsync(ClaimsPrincipal user, PanopticonDb db, CancellationToken cancellationToken)
    {
        var me = user.GetRequiredMemberId();
        var myProjects = db.ProjectMembers.Where(m => m.MemberId == me).Select(m => m.ProjectId);
        var members = await db.Members.AsNoTracking()
            .Where(m => m.RemovedAt == null
                && (m.Id == me || db.ProjectMembers.Any(pm => pm.MemberId == m.Id && myProjects.Contains(pm.ProjectId))))
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.EmailKey)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(members.Select(MemberView.From).ToList());
    }

    internal static async Task<IResult> RenameAsync(
        RenameRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        CancellationToken cancellationToken)
    {
        var displayName = MemberNames.Validate(request.DisplayName);
        if (displayName is null)
        {
            return BadName();
        }

        var memberId = user.GetMemberId();
        var member = await db.Members.SingleAsync(m => m.Id == memberId, cancellationToken);
        member.DisplayName = displayName;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(MemberView.From(member));
    }

    private static IResult BadName() =>
        SignInEndpoints.Problem(
            HttpStatusCode.BadRequest,
            $"A display name must be 1 to {MemberNames.MaxLength} characters, not counting spaces at either end.");
}
