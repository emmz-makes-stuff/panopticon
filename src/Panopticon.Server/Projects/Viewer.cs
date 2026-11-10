using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;

namespace Panopticon.Server.Projects;

/// <summary>A change's scope (projects D1): a Project, a member's personal work, or neither (a bootstrap report).</summary>
public sealed record ChangeScope(Guid? ProjectId, Guid? OwnerMemberId)
{
    /// <summary>A bootstrap report's scope, with no Project and no owner: every member sees it.</summary>
    public static ChangeScope Bootstrap { get; } = new(null, null);
}

/// <summary>
/// Who is looking (projects D2): a signed-in member and the Projects they belong to, in any role. Built once per request
/// and handed to every change read, so a change outside the viewer's scopes is never read at all.
/// </summary>
public sealed class Viewer
{
    public Viewer(Guid memberId, IEnumerable<Guid> projectIds)
    {
        MemberId = memberId;
        ProjectIds = [.. projectIds];
    }

    public Guid MemberId { get; }

    /// <summary>The Projects the member belongs to. A list, not a set, so EF can translate <c>Contains</c> on it.</summary>
    public IReadOnlyList<Guid> ProjectIds { get; }

    /// <summary>
    /// Whether a change in this scope is visible to the viewer: the in-memory twin of
    /// <see cref="Visibility.VisibleTo"/>, for the live stream (D3). The two must say the same.
    /// </summary>
    public bool CanSee(ChangeScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return (scope.ProjectId is { } project && ProjectIds.Contains(project))
            || scope.OwnerMemberId == MemberId
            || scope is { ProjectId: null, OwnerMemberId: null };
    }

    /// <summary>The viewer of a member, with their Projects as they are now.</summary>
    public static async Task<Viewer> LoadAsync(PanopticonDb db, Guid memberId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var projectIds = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .Select(m => m.ProjectId)
            .ToListAsync(cancellationToken);
        return new Viewer(memberId, projectIds);
    }

    /// <summary>
    /// Binds an endpoint's <see cref="Viewer"/> parameter from the session's member. Only endpoints behind the Member
    /// policy take one, so there's always a member.
    /// </summary>
    public static async ValueTask<Viewer?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var db = context.RequestServices.GetRequiredService<PanopticonDb>();
        return await LoadAsync(db, context.User.GetRequiredMemberId(), context.RequestAborted);
    }
}

/// <summary>The one visibility rule (projects D2; spec change-visibility, Who sees a change).</summary>
public static class Visibility
{
    /// <summary>
    /// The changes the viewer may see: those in one of their Projects, their own personal ones, and bootstrap ones
    /// (no Project and no owner), which everyone sees.
    /// </summary>
    public static IQueryable<Change> VisibleTo(this IQueryable<Change> changes, Viewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var (memberId, projectIds) = (viewer.MemberId, viewer.ProjectIds);
        return changes.Where(c =>
            (c.ProjectId != null && projectIds.Contains(c.ProjectId.Value))
            || c.OwnerMemberId == memberId
            || (c.ProjectId == null && c.OwnerMemberId == null));
    }
}
