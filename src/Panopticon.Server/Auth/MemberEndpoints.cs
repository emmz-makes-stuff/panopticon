using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

public sealed record InviteRequest(string? Email, string? DisplayName);

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
/// Listing, inviting, renaming and removing members (spec team-members; design D11). Every endpoint needs a session; the
/// state-changing ones also need the D6 header, which <see cref="RequestHeaderMiddleware"/> enforces for all of <c>/api</c>.
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
    /// Removes another member (spec team-members, "Removing a member"). In one transaction it sets
    /// <c>removed_at</c>, deletes their sessions, revokes their API tokens, and deletes their unused sign-in links, so
    /// a link sent before removal can't sign them in if they're invited back. Their open dashboard streams end on the
    /// next keep-alive check (design D11). The row stays, so their past reports keep their name.
    /// </summary>
    internal static async Task<IResult> RemoveAsync(
        Guid id,
        ClaimsPrincipal user,
        PanopticonDb db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (id == user.GetMemberId())
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "You can't remove yourself. Ask another member to.");
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var removed = await db.Members
            .Where(m => m.Id == id && m.RemovedAt == null)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, now), cancellationToken);
        if (removed == 0)
        {
            return SignInEndpoints.Problem(HttpStatusCode.NotFound, "There's no such member.");
        }

        await db.Sessions.Where(s => s.MemberId == id).ExecuteDeleteAsync(cancellationToken);
        await db.SignInLinks.Where(l => l.MemberId == id && l.UsedAt == null).ExecuteDeleteAsync(cancellationToken);
        await db.ApiTokens
            .Where(t => t.MemberId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal static async Task<IResult> ListAsync(PanopticonDb db, CancellationToken cancellationToken)
    {
        var members = await db.Members.AsNoTracking()
            .Where(m => m.RemovedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.EmailKey)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(members.Select(MemberView.From).ToList());
    }

    /// <summary>
    /// Adds the address as a member, or makes its removed member active again (keeping their id, so their past
    /// reports stay theirs). No email is sent: the invited person signs in like anyone else.
    /// </summary>
    internal static async Task<IResult> InviteAsync(
        InviteRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!MemberEmail.IsValid(request.Email))
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "That isn't a valid email address.");
        }

        string? displayName = null;
        if (request.DisplayName is not null)
        {
            displayName = MemberNames.Validate(request.DisplayName);
            if (displayName is null)
            {
                return BadName();
            }
        }

        var key = MemberEmail.Key(request.Email!);
        var existing = await db.Members.SingleOrDefaultAsync(m => m.EmailKey == key, cancellationToken);
        if (existing is { IsActive: true })
        {
            return AlreadyMember(existing.Email);
        }

        var inviter = user.GetMemberId();
        var member = existing;
        if (member is null)
        {
            member = new Member
            {
                Email = MemberEmail.Clean(request.Email!),
                EmailKey = key,
                DisplayName = displayName ?? MemberEmail.DefaultDisplayName(request.Email!),
                CreatedAt = time.GetUtcNow(),
                InvitedByMemberId = inviter,
            };
            db.Members.Add(member);
        }
        else
        {
            member.RemovedAt = null;
            member.InvitedByMemberId = inviter;
            if (displayName is not null)
            {
                member.DisplayName = displayName;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (existing is null
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone invited the same address at the same moment; theirs won.
            return AlreadyMember(MemberEmail.Clean(request.Email!));
        }

        return TypedResults.Created($"{MembersPath}/{member.Id}", MemberView.From(member));
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

    private static IResult AlreadyMember(string email) =>
        SignInEndpoints.Problem(HttpStatusCode.Conflict, $"{email} is already a member.");

    private static IResult BadName() =>
        SignInEndpoints.Problem(
            HttpStatusCode.BadRequest,
            $"A display name must be 1 to {MemberNames.MaxLength} characters, not counting spaces at either end.");
}
