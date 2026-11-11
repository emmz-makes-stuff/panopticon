using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Auth;

public sealed record RenameRequest(string? DisplayName);

public sealed record ServerInviteRequest(string? Email);

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
/// Listing, inviting, renaming and removing members (spec team-members; design D11, projects D8, server-invites). People
/// join through a Project's invite, or an initial member's invite to the server. Every endpoint needs a session; the
/// state-changing ones also need the D6 header, which <see cref="RequestHeaderMiddleware"/> enforces for all of
/// <c>/api</c>.
/// </summary>
public static class MemberEndpoints
{
    public const string MembersPath = "/api/members";
    public const string MePath = "/api/members/me";

    public static IEndpointRouteBuilder MapMembers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(MembersPath, ListAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPost(MembersPath, InviteAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPatch(MePath, RenameAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapDelete($"{MembersPath}/{{id:guid}}", RemoveAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    /// <summary>
    /// An initial member invites an address to the server, in no Project (spec team-members, "Inviting to the server";
    /// server-invites D1). It makes a new member, or a removed one active again, with the same locking read as Project
    /// invites (<see cref="MemberInvites.AddUnlessActiveAsync"/>), and emails them once the invite has committed.
    /// </summary>
    internal static async Task<IResult> InviteAsync(
        ServerInviteRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        PanopticonDb db,
        InitialMemberList initialMembers,
        AuthLimits limits,
        EmailOutbox emails,
        IOptions<PublicUrlOptions> publicUrl,
        IHostEnvironment environment,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(publicUrl);
        var inviterId = user.GetRequiredMemberId();
        if (!await CanRemoveMembersAsync(db, initialMembers, inviterId, cancellationToken))
        {
            return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "Only an initial member can invite people to the server.");
        }

        if (!MemberEmail.IsValid(request.Email))
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "That isn't a valid email address.");
        }

        // D3: before any database work, so a flood of refused requests costs nothing.
        if (!limits.TryServerInvite(inviterId))
        {
            return SignInEndpoints.Problem(HttpStatusCode.TooManyRequests, "You've sent a lot of invites. Try again later.");
        }

        var email = request.Email!;
        var key = MemberEmail.Key(email);
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The inviter's row and the invited address's row, in id order, as a removal locks member rows, so the two can't
        // deadlock. A removal of the inviter that commits first is seen here (review fix 12.5).
        var locked = await db.Members
            .FromSql($"SELECT * FROM members WHERE id = {inviterId} OR email_key = {key} ORDER BY id FOR NO KEY UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var inviter = locked.SingleOrDefault(m => m.Id == inviterId);
        if (inviter is not { IsActive: true })
        {
            return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "You've been removed from the server.");
        }

        if (locked.SingleOrDefault(m => m.EmailKey == key) is { IsActive: true } existing)
        {
            return AlreadyAMember(existing.Email);
        }

        Member? member;
        try
        {
            // This read is a new statement, so it sees a member that a concurrent invite committed while this one waited
            // on the inviter's lock, which the read above can't.
            member = await MemberInvites.AddUnlessActiveAsync(db, email, inviterId, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another invite, to the server or a Project, added the same new address at the same moment.
            return AlreadyAMember(MemberEmail.Clean(email));
        }

        if (member is null)
        {
            return AlreadyAMember(MemberEmail.Clean(email));
        }

        await transaction.CommitAsync(cancellationToken);

        // Only after the commit, so a refused or rolled-back invite never sends an email.
        var signInPage = SignInEndpoints.LinkRoot(publicUrl.Value, context, environment);
        emails.Enqueue(new ServerInviteJob(ServerInviteEmail.Create(member.Email, inviter.DisplayName, signInPage)));
        return TypedResults.Created($"{MembersPath}/{member.Id}", MemberView.From(member));
    }

    /// <summary>
    /// Removes another member from the server (spec team-members, "Removing a member"); only an initial member may. In
    /// one transaction it sets <c>removed_at</c>, deletes their sessions, revokes their API tokens, deletes their unused
    /// sign-in links (so a link sent before removal can't sign them in if they're invited back), and takes them out of
    /// every Project. It's refused while they're the last owner of a Project, naming it (projects D8). Their open
    /// dashboard streams end at once (projects D3, §11). The row stays, so their past reports keep their
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

        // Review fix 12.4: locks in the Project endpoints' order, Projects first and then members, so a removal and an
        // invite can't deadlock. First every Project they belong to, in id order, as the Project endpoints lock them, so
        // no role change or removal there can slip in between the last-owner check and the delete.
        var locked = await db.Projects
            .FromSql($"""
                SELECT * FROM projects
                WHERE id IN (SELECT project_id FROM project_members WHERE member_id = {id})
                ORDER BY id
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var lockedIds = locked.Select(p => p.Id).ToHashSet();

        // Then every active member row (in id order, so two removals can't deadlock). Two members removing each other at
        // once then take turns: the second sees that its remover has gone, so the team never ends up with nobody. FOR NO
        // KEY UPDATE, since only removed_at changes: the foreign keys' KEY SHARE checks (a report, an invite) don't wait.
        var active = await db.Members
            .FromSql($"SELECT * FROM members WHERE removed_at IS NULL ORDER BY id FOR NO KEY UPDATE")
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

        // A fresh read, now that the locks are held. With their member row locked, they can't join another Project: an invite
        // or a new Project locks that row first (review fix 12.5), so it waits on this transaction, then sees them removed. But one may have committed just
        // before the lock: that Project isn't locked, and a Project can't be locked after a member, so it's a 409.
        var memberships = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.MemberId == id)
            .Select(m => new { m.ProjectId, m.Role })
            .ToListAsync(cancellationToken);
        if (memberships.Any(m => !lockedIds.Contains(m.ProjectId)))
        {
            return SignInEndpoints.Problem(
                HttpStatusCode.Conflict,
                "They joined a Project while you were removing them. Try again.");
        }

        // projects D8: never leave a Project without an owner, checked under the Project locks.
        var ownedIds = memberships.Where(m => m.Role == ProjectRoles.Owner).Select(m => m.ProjectId).ToList();
        var coOwned = await db.ProjectMembers
            .Where(m => ownedIds.Contains(m.ProjectId) && m.MemberId != id && m.Role == ProjectRoles.Owner)
            .Select(m => m.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var lastOwnerOf = await db.Projects.AsNoTracking()
            .Where(p => ownedIds.Contains(p.Id) && !coOwned.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(cancellationToken);
        lastOwnerOf.Sort(StringComparer.Ordinal);
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
    /// projects D8). An initial member gets every active member instead, so they can see and remove the people they
    /// invited to the server (server-invites D4).
    /// </summary>
    internal static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        PanopticonDb db,
        InitialMemberList initialMembers,
        CancellationToken cancellationToken)
    {
        var me = user.GetRequiredMemberId();
        var active = db.Members.AsNoTracking().Where(m => m.RemovedAt == null);
        if (!await CanRemoveMembersAsync(db, initialMembers, me, cancellationToken))
        {
            var myProjects = db.ProjectMembers.Where(m => m.MemberId == me).Select(m => m.ProjectId);
            active = active.Where(m =>
                m.Id == me || db.ProjectMembers.Any(pm => pm.MemberId == m.Id && myProjects.Contains(pm.ProjectId)));
        }

        var members = await active
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

    private static IResult AlreadyAMember(string email) =>
        SignInEndpoints.Problem(HttpStatusCode.Conflict, $"{email} is already a member.");

    private static IResult BadName() =>
        SignInEndpoints.Problem(
            HttpStatusCode.BadRequest,
            $"A display name must be 1 to {MemberNames.MaxLength} characters, not counting spaces at either end.");
}
