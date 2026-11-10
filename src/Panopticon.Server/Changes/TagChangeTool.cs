using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Changes;

/// <summary>
/// The online MCP endpoint's <c>tag_change</c> tool (spec tags, "Tagging a change over MCP", "Agents only use existing
/// tags"; design D4). Every check runs before anything is written, and any failure changes nothing.
/// </summary>
[McpServerToolType]
public sealed class TagChangeTool
{
    [McpServerTool(Name = ContractsAssembly.TagChangeToolName, Destructive = false, Idempotent = true)]
    [Description("Put a Project's tags on one of its changes, or take them off. Only the Project's existing tags can be used: list_projects lists them. Needs an owner's or editor's token.")]
    public static async Task<CallToolResult> TagChangeAsync(
        PanopticonDb db,
        ChangeCards cards,
        ChangeBroadcaster broadcaster,
        RequestContext<CallToolRequestParams> context,
        [Description("Required. The OpenSpec change name.")] string? change = null,
        [Description("The tags to put on the change, as list_projects lists them, e.g. \"bug\" or \"area: onboarding\".")] IReadOnlyList<string>? add = null,
        [Description("The tags to take off the change.")] IReadOnlyList<string>? remove = null,
        [Description("Required. The id of the change's Project, from the repo's .panopticon.json.")] string? project = null,
        [Description("Required. The change's repo: host, owner (may contain '/') and name.")] RepoRef? repo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(change))
        {
            throw new McpException("Say which change to tag: \"change\" is missing.");
        }

        if (repo is null || string.IsNullOrWhiteSpace(repo.Host) || string.IsNullOrWhiteSpace(repo.Owner) || string.IsNullOrWhiteSpace(repo.Name))
        {
            throw new McpException("Say which repo the change is in: \"repo\" needs a host, owner and name.");
        }

        if (!Guid.TryParse(project, out var projectId))
        {
            throw new McpException(
                $"\"project\" must be a Project's id{(project is null ? string.Empty : $", not \"{project}\"")}. list_projects lists your Projects.");
        }

        var adding = Parse(add);
        var removing = Parse(remove);
        if (adding.Count == 0 && removing.Count == 0)
        {
            throw new McpException("Say which tags to add or remove.");
        }

        // A member's token tags as that member; the Development bootstrap token, with no member, may tag any change (D4).
        // The same role lookup ChangeTagging.ApplyAsync checks again inside its transaction: this one gives the agent a
        // precise message, and that one catches a role changed in between.
        var memberId = context.User?.GetMemberId();
        var code = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Code)
            .SingleOrDefaultAsync(cancellationToken);
        var role = code is null ? null
            : memberId is { } member ? await ProjectEndpoints.RoleAsync(db, projectId, member, cancellationToken)
            : ProjectRoles.Owner;
        var found = role is null ? null : new { Code = code!, Role = role };
        if (found is null)
        {
            throw new McpException($"You aren't a member of a Project with id {projectId}. list_projects lists yours.");
        }

        if (!ProjectTagEndpoints.CanManageTags(found.Role))
        {
            throw new McpException(
                $"Your role in {found.Code} is {found.Role}, and a {found.Role} can't tag changes. Ask one of its owners to make you an editor.");
        }

        var target = await FindChangeAsync(db, projectId, repo, change, cancellationToken)
            ?? throw new McpException($"There's no change \"{change.Trim()}\" in {found.Code} for {repo.Host}/{repo.Owner}/{repo.Name}.");

        var tags = (await db.ProjectTags.AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .ToListAsync(cancellationToken))
            .InTagOrder(t => t.KeyKey, t => t.ValueKey)
            .ToList();
        var ids = tags.ToDictionary(t => new Tag(t.Key, t.Value), t => t.Id);
        if (adding.Concat(removing).FirstOrDefault(t => !ids.ContainsKey(t)) is { } unknown)
        {
            var known = tags.Count == 0
                ? "It has no tags yet: an owner or editor can add them on its page."
                : $"Its tags are: {string.Join(", ", tags.Select(t => new Tag(t.Key, t.Value).Text))}.";
            throw new McpException($"{found.Code} has no tag \"{unknown.Text}\", and tag_change can only use existing tags. {known}");
        }

        var outcome = await ChangeTagging.ApplyAsync(
            db,
            target.Id,
            projectId,
            memberId,
            [.. adding.Select(t => ids[t])],
            [.. removing.Select(t => ids[t])],
            cancellationToken);
        switch (outcome)
        {
            case TaggingOutcome.NotAllowed:
                throw new McpException($"Your role in {found.Code} changed, and you can no longer tag its changes.");
            case TaggingOutcome.UnknownTag:
                throw new McpException($"One of those tags was just deleted from {found.Code}. Call list_projects for its tags now.");
        }

        var (card, archived) = await cards.GetIncludingArchivedAsync(target.Id, cancellationToken);
        if (!archived)
        {
            broadcaster.Publish(card, new ChangeScope(projectId, null));
        }

        var result = new TagChangeResult(
            target.Id.ToString(),
            card.Card.Change,
            found.Code,
            [.. card.Card.ProjectTags.Select(t => new Tag(t.Key, t.Value).Text)]);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Describe() }],
            StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.TagChangeResult),
        };
    }

    /// <summary>Each text as a tag, duplicates collapsed (TagText.TryParseAll); a malformed one is the agent's error.</summary>
    private static IReadOnlyList<Tag> Parse(IReadOnlyList<string>? texts) =>
        TagText.TryParseAll(texts ?? [], TagText.MaxProjectTags, out var tags, out var error)
            ? tags
            : throw new McpException(error);

    /// <summary>The Project's active change of that repo and name, or else its most recently archived one (D4).</summary>
    private static Task<Change?> FindChangeAsync(PanopticonDb db, Guid projectId, RepoRef repo, string name, CancellationToken cancellationToken)
    {
        var (host, owner, repoName, nameKey) = (IdentityKey.Of(repo.Host), IdentityKey.Of(repo.Owner), IdentityKey.Of(repo.Name), IdentityKey.Of(name));
        return db.Changes.AsNoTracking()
            .Where(c => c.ProjectId == projectId
                && c.NameKey == nameKey
                && c.Repo.HostKey == host && c.Repo.OwnerKey == owner && c.Repo.NameKey == repoName)
            .OrderBy(c => c.ArchivedAt != null)
            .ThenByDescending(c => c.ArchivedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
