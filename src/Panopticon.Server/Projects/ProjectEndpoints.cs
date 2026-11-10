using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Live;

namespace Panopticon.Server.Projects;

public sealed record CreateProjectRequest(string? Name, string? Code);

public sealed record RenameProjectRequest(string? Name);

public sealed record InviteToProjectRequest(string? Email, string? Role);

public sealed record ChangeRoleRequest(string? Role);

/// <summary>A Project as its member sees it in the list, with their own role.</summary>
public sealed record ProjectView(Guid Id, string Name, string Code, string Role);

/// <summary>A member of a Project, as its page lists them.</summary>
public sealed record ProjectMemberView(Guid MemberId, string Email, string DisplayName, string Role, DateTimeOffset JoinedAt);

/// <summary>A Project's page: the Project, the viewer's role, and its members (owners first, then by when they joined).</summary>
public sealed record ProjectDetails(Guid Id, string Name, string Code, string Role, IReadOnlyList<ProjectMemberView> Members);

/// <summary>
/// Projects and their members (spec projects; design D5). Every endpoint needs a session. A Project the caller doesn't
/// belong to answers 404, so its existence isn't leaked; one they belong to but may not change answers 403.
/// </summary>
public static class ProjectEndpoints
{
    public const string ProjectsPath = "/api/projects";
    public const int NameMaxLength = 100;

    public static string ProjectPath(Guid id) => $"{ProjectsPath}/{id}";

    public static string MembersPath(Guid id) => $"{ProjectsPath}/{id}/members";

    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ProjectsPath, ListAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPost(ProjectsPath, CreateAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapGet($"{ProjectsPath}/{{id:guid}}", GetAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPatch($"{ProjectsPath}/{{id:guid}}", RenameAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPost($"{ProjectsPath}/{{id:guid}}/members", InviteAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPatch($"{ProjectsPath}/{{id:guid}}/members/{{memberId:guid}}", ChangeRoleAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapDelete($"{ProjectsPath}/{{id:guid}}/members/{{memberId:guid}}", RemoveAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    /// <summary>The caller's Projects, by code (spec projects, "Projects list").</summary>
    internal static async Task<IResult> ListAsync(ClaimsPrincipal user, PanopticonDb db, CancellationToken cancellationToken)
    {
        var projects = await MemberProjects.Of(db, user.GetRequiredMemberId())
            .Select(p => new ProjectView(p.Id, p.Name, p.Code, p.Role))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(projects);
    }

    /// <summary>Creates a Project with the caller as its owner (spec projects, "Creating a Project", "Project codes").</summary>
    internal static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var name = ProjectNames.Validate(request.Name);
        if (name is null)
        {
            return BadName();
        }

        var code = ProjectCodes.Validate(request.Code);
        if (code is null)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, ProjectCodes.Rule);
        }

        var memberId = user.GetRequiredMemberId();
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Review fix 12.5: like an invite, it locks the member's row, so a removal from the server that commits first is
        // seen, and a removed member never ends up owning a Project.
        var active = await db.Members
            .FromSql($"SELECT * FROM members WHERE id = {memberId} FOR NO KEY UPDATE")
            .AsNoTracking()
            .AnyAsync(m => m.RemovedAt == null, cancellationToken);
        if (!active)
        {
            return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "You've been removed from the server.");
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = code,
            CodeKey = ProjectCodes.Key(code),
            CreatedAt = now,
            CreatedByMemberId = memberId,
        };
        project.Members.Add(new ProjectMember { MemberId = memberId, Role = ProjectRoles.Owner, JoinedAt = now });
        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return SignInEndpoints.Problem(HttpStatusCode.Conflict, $"The code {code} is taken. Choose another.");
        }

        await transaction.CommitAsync(cancellationToken);
        broadcaster.PublishMembershipChanged(memberId, project.Id, isMember: true);
        return TypedResults.Created(ProjectPath(project.Id), new ProjectView(project.Id, project.Name, project.Code, ProjectRoles.Owner));
    }

    internal static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, PanopticonDb db, CancellationToken cancellationToken)
    {
        var details = await DetailsAsync(db, id, user.GetRequiredMemberId(), cancellationToken);
        return details is null ? NoSuchProject() : TypedResults.Ok(details);
    }

    /// <summary>An owner renames the Project; its id and code never change (spec projects, "Project names").</summary>
    internal static async Task<IResult> RenameAsync(
        Guid id,
        RenameProjectRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        CancellationToken cancellationToken)
    {
        var role = await RoleAsync(db, id, user.GetRequiredMemberId(), cancellationToken);
        if (role is null)
        {
            return NoSuchProject();
        }

        if (role != ProjectRoles.Owner)
        {
            return OwnersOnly();
        }

        var name = ProjectNames.Validate(request.Name);
        if (name is null)
        {
            return BadName();
        }

        await db.Projects.Where(p => p.Id == id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Name, name), cancellationToken);
        return TypedResults.Ok(await DetailsAsync(db, id, user.GetRequiredMemberId(), cancellationToken));
    }

    /// <summary>
    /// An owner invites an address with a role (spec projects, "Inviting to a Project"). An address that can't sign in
    /// yet becomes an active member of the server, so it can. No email is sent.
    /// </summary>
    internal static async Task<IResult> InviteAsync(
        Guid id,
        InviteToProjectRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var callerId = user.GetRequiredMemberId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var caller = await LockAsync(db, id, callerId, cancellationToken);
        if (caller is not null)
        {
            return caller;
        }

        if (!MemberEmail.IsValid(request.Email))
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "That isn't a valid email address.");
        }

        if (!IsRole(request.Role))
        {
            return BadRole();
        }

        var now = time.GetUtcNow();
        Member member;
        try
        {
            member = await MemberInvites.FindOrAddAsync(db, request.Email!, callerId, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone invited the same new address, to some Project, at the same moment.
            return SignInEndpoints.Problem(
                HttpStatusCode.Conflict,
                $"{MemberEmail.Clean(request.Email!)} was just invited elsewhere. Try again.");
        }

        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == id && m.MemberId == member.Id, cancellationToken))
        {
            return SignInEndpoints.Problem(HttpStatusCode.Conflict, $"{member.Email} is already in this Project.");
        }

        var membership = new ProjectMember
        {
            ProjectId = id,
            MemberId = member.Id,
            Role = request.Role!,
            JoinedAt = now,
            InvitedByMemberId = callerId,
        };
        db.ProjectMembers.Add(membership);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        broadcaster.PublishMembershipChanged(member.Id, id, isMember: true);
        return TypedResults.Created(
            $"{MembersPath(id)}/{member.Id}",
            new ProjectMemberView(member.Id, member.Email, member.DisplayName, membership.Role, now));
    }

    /// <summary>An owner changes a member's role, never leaving the Project without an owner (spec projects).</summary>
    internal static async Task<IResult> ChangeRoleAsync(
        Guid id,
        Guid memberId,
        ChangeRoleRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var caller = await LockAsync(db, id, user.GetRequiredMemberId(), cancellationToken);
        if (caller is not null)
        {
            return caller;
        }

        if (!IsRole(request.Role))
        {
            return BadRole();
        }

        var membership = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == id && m.MemberId == memberId, cancellationToken);
        if (membership is null)
        {
            return NoSuchMember();
        }

        if (membership.Role == ProjectRoles.Owner && request.Role != ProjectRoles.Owner
            && await OwnerCountAsync(db, id, cancellationToken) == 1)
        {
            return NeedsAnOwner();
        }

        membership.Role = request.Role!;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        broadcaster.PublishMembershipChanged(memberId, id, isMember: true);
        return TypedResults.Ok(await DetailsAsync(db, id, user.GetRequiredMemberId(), cancellationToken));
    }

    /// <summary>
    /// An owner removes a member, or a member leaves (spec projects, "Changing roles and removing"). The last owner
    /// can't go. Their open pages drop the Project at once (design D3, §11).
    /// </summary>
    internal static async Task<IResult> RemoveAsync(
        Guid id,
        Guid memberId,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        var callerId = user.GetRequiredMemberId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var caller = await LockAsync(db, id, callerId, cancellationToken, allowSelf: memberId == callerId);
        if (caller is not null)
        {
            return caller;
        }

        var membership = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == id && m.MemberId == memberId, cancellationToken);
        if (membership is null)
        {
            return NoSuchMember();
        }

        if (membership.Role == ProjectRoles.Owner && await OwnerCountAsync(db, id, cancellationToken) == 1)
        {
            return NeedsAnOwner();
        }

        db.ProjectMembers.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        broadcaster.PublishMembershipChanged(memberId, id, isMember: false);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Locks the Project's row, so membership changes take turns and the owner count can't race, then checks the
    /// caller under that lock: null when they're an owner (or, with <paramref name="allowSelf"/>, any member acting on
    /// themselves); otherwise the response to send.
    /// </summary>
    private static async Task<IResult?> LockAsync(
        PanopticonDb db,
        Guid projectId,
        Guid callerId,
        CancellationToken cancellationToken,
        bool allowSelf = false)
    {
        var locked = await db.Projects.FromSql($"SELECT * FROM projects WHERE id = {projectId} FOR UPDATE")
            .AsNoTracking()
            .AnyAsync(cancellationToken);
        var role = locked ? await RoleAsync(db, projectId, callerId, cancellationToken) : null;
        if (role is null)
        {
            return NoSuchProject();
        }

        return role == ProjectRoles.Owner || allowSelf ? null : OwnersOnly();
    }

    private static Task<string?> RoleAsync(PanopticonDb db, Guid projectId, Guid memberId, CancellationToken cancellationToken) =>
        db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.MemberId == memberId)
            .Select(m => m.Role)
            .SingleOrDefaultAsync(cancellationToken);

    private static Task<int> OwnerCountAsync(PanopticonDb db, Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRoles.Owner, cancellationToken);

    private static async Task<ProjectDetails?> DetailsAsync(PanopticonDb db, Guid id, Guid viewerId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var members = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == id)
            .Select(m => new ProjectMemberView(m.MemberId, m.Member.Email, m.Member.DisplayName, m.Role, m.JoinedAt))
            .ToListAsync(cancellationToken);
        var role = members.FirstOrDefault(m => m.MemberId == viewerId)?.Role;
        if (role is null)
        {
            return null;
        }

        return new ProjectDetails(
            project.Id,
            project.Name,
            project.Code,
            role,
            [.. members.OrderBy(m => m.Role == ProjectRoles.Owner ? 0 : 1).ThenBy(m => m.JoinedAt).ThenBy(m => m.Email, StringComparer.OrdinalIgnoreCase)]);
    }

    private static bool IsRole(string? role) => role is not null && ProjectRoles.All.Contains(role);

    private static IResult NoSuchProject() => SignInEndpoints.Problem(HttpStatusCode.NotFound, "There's no such Project.");

    private static IResult NoSuchMember() =>
        SignInEndpoints.Problem(HttpStatusCode.NotFound, "That person isn't a member of this Project.");

    private static IResult OwnersOnly() =>
        SignInEndpoints.Problem(HttpStatusCode.Forbidden, "Only the Project's owners can do that.");

    private static IResult NeedsAnOwner() =>
        SignInEndpoints.Problem(HttpStatusCode.Conflict, "A Project needs an owner. Make someone else an owner first.");

    private static IResult BadRole() =>
        SignInEndpoints.Problem(HttpStatusCode.BadRequest, "A role is owner, editor or viewer.");

    private static IResult BadName() =>
        SignInEndpoints.Problem(HttpStatusCode.BadRequest, $"A Project needs a name of 1 to {NameMaxLength} characters.");
}

/// <summary>Project codes (spec projects, "Project codes"): 2 to 10 letters or digits, starting with a letter.</summary>
public static partial class ProjectCodes
{
    public const string Rule = "A code is 2 to 10 letters or digits, starting with a letter.";

    /// <summary>The code in capitals, or null when it breaks the rule.</summary>
    public static string? Validate(string? code)
    {
        var trimmed = code?.Trim();
        return trimmed is not null && Shape().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : null;
    }

    /// <summary>The case-insensitive key carrying the unique index.</summary>
    public static string Key(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}

/// <summary>Project names (spec projects, "Project names").</summary>
public static class ProjectNames
{
    /// <summary>The trimmed name, or null when it's blank or too long.</summary>
    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > ProjectEndpoints.NameMaxLength ? null : trimmed;
    }
}

/// <summary>Finding or adding the member an invite names (spec projects, "Inviting to a Project").</summary>
public static class MemberInvites
{
    /// <summary>
    /// The active member with this address: an existing one, a removed one made active again (keeping their id, so
    /// their past reports stay theirs), or a new one. Saves at once. If another invite adds the same new address at the
    /// same moment, the unique email key makes this save throw (the caller answers 409).
    /// </summary>
    public static async Task<Member> FindOrAddAsync(
        PanopticonDb db,
        string email,
        Guid inviterId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var key = MemberEmail.Key(email);

        // Locked (review fix 12.5): a removal from the server holding this row is waited for, then seen, so the invite makes
        // them active again rather than adding a removed member to the Project. FOR NO KEY UPDATE, as the removal takes it,
        // so the foreign keys' KEY SHARE checks (a report, another invite) never wait on it.
        var member = await db.Members
            .FromSql($"SELECT * FROM members WHERE email_key = {key} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (member is { IsActive: true })
        {
            return member;
        }

        if (member is not null)
        {
            member.RemovedAt = null;
            member.InvitedByMemberId = inviterId;
            await db.SaveChangesAsync(cancellationToken);
            return member;
        }

        member = new Member
        {
            Email = MemberEmail.Clean(email),
            EmailKey = key,
            DisplayName = MemberEmail.DefaultDisplayName(email),
            CreatedAt = now,
            InvitedByMemberId = inviterId,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member;
    }
}

/// <summary>A member's Project with their role in it.</summary>
public sealed record MemberProject(Guid Id, string Name, string Code, string Role);

/// <summary>A member's Projects, by code: the one query behind the Projects page and <c>list_projects</c> (review fix 12.6).</summary>
public static class MemberProjects
{
    public static IQueryable<MemberProject> Of(PanopticonDb db, Guid memberId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.ProjectMembers.AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => new { p.Id, p.Name, p.Code, p.CodeKey, m.Role })
            .OrderBy(p => p.CodeKey)
            .Select(p => new MemberProject(p.Id, p.Name, p.Code, p.Role));
    }
}
