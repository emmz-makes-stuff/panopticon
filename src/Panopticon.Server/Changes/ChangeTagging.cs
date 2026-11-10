using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Changes;

/// <summary>How a request to tag a change came out (tags D4).</summary>
public enum TaggingOutcome
{
    /// <summary>The change has the tags asked for, and not those asked to go.</summary>
    Done,

    /// <summary>The tagger isn't an owner or editor of the change's Project. Nothing changed.</summary>
    NotAllowed,

    /// <summary>A tag isn't one of the change's Project's tags (it may just have been deleted). Nothing changed.</summary>
    UnknownTag,
}

/// <summary>
/// Puts a Project's tags on one of its changes and takes them off (spec tags, "Tagging a change on the dashboard",
/// "Tagging a change over MCP"; design D4). The dashboard endpoint and the <c>tag_change</c> tool both come here, so the
/// role check and the writes are the same for both.
/// </summary>
public static class ChangeTagging
{
    /// <summary>
    /// In one transaction: takes the Project's row FOR SHARE (a tag write holds it FOR UPDATE, so a tag can't be deleted
    /// under us), and the change's row FOR UPDATE (so edits to one change take turns); checks <paramref name="taggerId"/>
    /// is an owner or editor (null is the Development bootstrap token, which may tag anything); checks every id is the
    /// Project's; then adds and removes. Adding a tag the change has, or removing one it lacks, is a no-op.
    /// </summary>
    public static async Task<TaggingOutcome> ApplyAsync(
        PanopticonDb db,
        Guid changeId,
        Guid projectId,
        Guid? taggerId,
        IReadOnlyCollection<Guid> add,
        IReadOnlyCollection<Guid> remove,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM projects WHERE id = {projectId} FOR SHARE", cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM changes WHERE id = {changeId} FOR UPDATE", cancellationToken);

        if (taggerId is { } tagger
            && !ProjectTagEndpoints.CanManageTags(await ProjectEndpoints.RoleAsync(db, projectId, tagger, cancellationToken)))
        {
            return TaggingOutcome.NotAllowed;
        }

        var wanted = add.Concat(remove).Distinct().ToList();
        var known = await db.ProjectTags.CountAsync(t => t.ProjectId == projectId && wanted.Contains(t.Id), cancellationToken);
        if (known != wanted.Count)
        {
            return TaggingOutcome.UnknownTag;
        }

        var removing = remove.Except(add).ToList();
        await db.ChangeProjectTags.Where(t => t.ChangeId == changeId && removing.Contains(t.TagId)).ExecuteDeleteAsync(cancellationToken);
        var has = await db.ChangeProjectTags.Where(t => t.ChangeId == changeId).Select(t => t.TagId).ToListAsync(cancellationToken);
        db.ChangeProjectTags.AddRange(add.Distinct().Except(has).Select(tagId => new ChangeProjectTag { ChangeId = changeId, TagId = tagId }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaggingOutcome.Done;
    }
}
