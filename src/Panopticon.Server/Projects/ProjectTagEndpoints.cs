using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Live;

namespace Panopticon.Server.Projects;

public sealed record TagRequest(string? Tag);

/// <summary>One of a Project's tags, as its page lists it, with how many changes (active or archived) carry it.</summary>
public sealed record ProjectTagView(Guid Id, string Key, string? Value, string Text, int ChangeCount);

/// <summary>A Project tag as cards and the live stream carry it: by id, so a rename or a delete can find it (tags D6).</summary>
public sealed record ProjectTagRef(Guid Id, string Key, string? Value);

/// <summary>
/// A Project's tags (spec tags, "A Project's tags", "Managing a Project's tags", "Renaming a tag", "Deleting a tag";
/// design D3). Every member sees them on the Project's page; owners and editors add, rename and delete them here.
/// </summary>
public static class ProjectTagEndpoints
{
    public static string TagsPath(Guid projectId) => $"{ProjectEndpoints.ProjectPath(projectId)}/tags";

    public static string TagPath(Guid projectId, Guid tagId) => $"{TagsPath(projectId)}/{tagId}";

    /// <summary>Whether a role may change a Project's tags and tag its changes (spec projects, "Membership roles").</summary>
    public static bool CanManageTags(string? role) => role is ProjectRoles.Owner or ProjectRoles.Editor;

    public static IEndpointRouteBuilder MapProjectTags(this IEndpointRouteBuilder endpoints)
    {
        var tags = $"{ProjectEndpoints.ProjectsPath}/{{id:guid}}/tags";
        endpoints.MapPost(tags, AddAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPatch($"{tags}/{{tagId:guid}}", RenameAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapDelete($"{tags}/{{tagId:guid}}", DeleteAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    /// <summary>A Project's tags by key, then value (a bare key first), each with its change count.</summary>
    public static async Task<IReadOnlyList<ProjectTagView>> ListAsync(PanopticonDb db, Guid projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.ProjectTags.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.KeyKey).ThenBy(t => t.ValueKey)
            .Select(t => new { t.Id, t.Key, t.Value, Count = db.ChangeProjectTags.Count(c => c.TagId == t.Id) })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(t => new ProjectTagView(t.Id, t.Key, t.Value, new Tag(t.Key, t.Value).Text, t.Count))];
    }

    internal static async Task<IResult> AddAsync(
        Guid id,
        TagRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (refusal, code) = await LockAsync(db, id, user.GetRequiredMemberId(), cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!TagText.TryParse(request.Tag, out var tag, out var error))
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, error);
        }

        if (await ExistingAsync(db, id, tag, except: null, cancellationToken) is { } existing)
        {
            return Duplicate(code, existing);
        }

        if (await db.ProjectTags.CountAsync(t => t.ProjectId == id, cancellationToken) >= TagText.MaxProjectTags)
        {
            return SignInEndpoints.Problem(
                HttpStatusCode.Conflict,
                $"This Project has {TagText.MaxProjectTags} tags, the most it can have. Delete one first.");
        }

        var row = new ProjectTag
        {
            Id = Guid.NewGuid(),
            ProjectId = id,
            Key = tag.Key,
            Value = tag.Value,
            KeyKey = tag.KeyKey,
            ValueKey = tag.ValueKey,
            CreatedAt = time.GetUtcNow(),
        };
        db.ProjectTags.Add(row);
        if (await SaveAsync(db, code, tag, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        await PublishAsync(db, broadcaster, id, cancellationToken);
        return TypedResults.Created(TagPath(id, row.Id), new ProjectTagView(row.Id, tag.Key, tag.Value, tag.Text, 0));
    }

    /// <summary>Renames a tag in place, so every change carrying it shows the new name (spec tags, "Renaming a tag").</summary>
    internal static async Task<IResult> RenameAsync(
        Guid id,
        Guid tagId,
        TagRequest request,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (refusal, code) = await LockAsync(db, id, user.GetRequiredMemberId(), cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!TagText.TryParse(request.Tag, out var tag, out var error))
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, error);
        }

        var row = await db.ProjectTags.SingleOrDefaultAsync(t => t.Id == tagId && t.ProjectId == id, cancellationToken);
        if (row is null)
        {
            return NoSuchTag();
        }

        // Renaming to an equal tag in other casing is fine: it's this tag.
        if (await ExistingAsync(db, id, tag, except: tagId, cancellationToken) is { } existing)
        {
            return Duplicate(code, existing);
        }

        (row.Key, row.Value, row.KeyKey, row.ValueKey) = (tag.Key, tag.Value, tag.KeyKey, tag.ValueKey);
        if (await SaveAsync(db, code, tag, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        await PublishAsync(db, broadcaster, id, cancellationToken);
        var count = await db.ChangeProjectTags.CountAsync(c => c.TagId == tagId, cancellationToken);
        return TypedResults.Ok(new ProjectTagView(tagId, tag.Key, tag.Value, tag.Text, count));
    }

    /// <summary>Deletes a tag, taking it off every change, active or archived (spec tags, "Deleting a tag").</summary>
    internal static async Task<IResult> DeleteAsync(
        Guid id,
        Guid tagId,
        ClaimsPrincipal user,
        PanopticonDb db,
        ChangeBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (refusal, _) = await LockAsync(db, id, user.GetRequiredMemberId(), cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        // change_project_tags rows go with it (a cascade, tags D2).
        var deleted = await db.ProjectTags.Where(t => t.Id == tagId && t.ProjectId == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return NoSuchTag();
        }

        await transaction.CommitAsync(cancellationToken);
        await PublishAsync(db, broadcaster, id, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Locks the Project's row, so tag writes take turns and the 200-tag limit holds (design D3), then checks the caller
    /// under that lock. Returns the response to send, or null and the Project's code.
    /// </summary>
    private static async Task<(IResult? Refusal, string Code)> LockAsync(
        PanopticonDb db,
        Guid projectId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var code = await db.Projects.FromSql($"SELECT * FROM projects WHERE id = {projectId} FOR UPDATE")
            .AsNoTracking()
            .Select(p => p.Code)
            .SingleOrDefaultAsync(cancellationToken);
        var role = code is null ? null : await ProjectEndpoints.RoleAsync(db, projectId, callerId, cancellationToken);
        if (role is null)
        {
            return (ProjectEndpoints.NoSuchProject(), string.Empty);
        }

        return CanManageTags(role) ? (null, code!) : (EditorsOnly(), code!);
    }

    /// <summary>Tells the Project's open pages its tags as they are now (tags D6).</summary>
    private static async Task PublishAsync(PanopticonDb db, ChangeBroadcaster broadcaster, Guid projectId, CancellationToken cancellationToken)
    {
        var tags = await db.ProjectTags.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.KeyKey).ThenBy(t => t.ValueKey)
            .Select(t => new ProjectTagRef(t.Id, t.Key, t.Value))
            .ToListAsync(cancellationToken);
        broadcaster.PublishProjectTags(projectId, tags);
    }

    private static Task<ProjectTag?> ExistingAsync(PanopticonDb db, Guid projectId, Tag tag, Guid? except, CancellationToken cancellationToken) =>
        db.ProjectTags.AsNoTracking().SingleOrDefaultAsync(
            t => t.ProjectId == projectId && t.KeyKey == tag.KeyKey && t.ValueKey == tag.ValueKey && t.Id != except,
            cancellationToken);

    /// <summary>Saves, answering 409 if the unique index caught a duplicate the check above couldn't see.</summary>
    private static async Task<IResult?> SaveAsync(PanopticonDb db, string code, Tag tag, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return SignInEndpoints.Problem(HttpStatusCode.Conflict, $"{code}/{tag.Text} is already one of this Project's tags.");
        }
    }

    private static IResult Duplicate(string code, ProjectTag existing) =>
        SignInEndpoints.Problem(
            HttpStatusCode.Conflict,
            $"{code}/{new Tag(existing.Key, existing.Value).Text} is already one of this Project's tags.");

    private static IResult NoSuchTag() => SignInEndpoints.Problem(HttpStatusCode.NotFound, "There's no such tag in this Project.");

    internal static IResult EditorsOnly() =>
        SignInEndpoints.Problem(HttpStatusCode.Forbidden, "Only the Project's owners and editors can change its tags.");
}
