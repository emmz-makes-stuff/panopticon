using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;

namespace Panopticon.Server.Projects;

/// <summary>The online MCP endpoint's <c>list_projects</c> tool (projects D6; spec projects, Listing Projects over MCP).</summary>
[McpServerToolType]
public sealed class ListProjectsTool
{
    [McpServerTool(Name = ContractsAssembly.ListProjectsToolName, Destructive = false, Idempotent = true, ReadOnly = true)]
    [Description("List the Projects you can report into: each one's id, name, code, your role, and its tags (which tag_change can put on its changes). Put a Project's id in the repo's .panopticon.json, as {\"project\": \"<id>\"}, to link the repo's work to it.")]
    public static async Task<CallToolResult> ListProjectsAsync(
        PanopticonDb db,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A member's token lists their own Projects; the Development bootstrap token, with no member, lists every
        // Project as a viewer, since it may report into any of them (D4).
        var projects = context.User?.GetMemberId() is { } memberId
            ? (await MemberProjects.Of(db, memberId).ToListAsync(cancellationToken))
                .ConvertAll(p => new ProjectSummary(p.Id.ToString(), p.Name, p.Code, p.Role))
            : await db.Projects.AsNoTracking()
                .OrderBy(p => p.CodeKey)
                .Select(p => new ProjectSummary(p.Id.ToString(), p.Name, p.Code, ProjectRoles.Viewer))
                .ToListAsync(cancellationToken);

        // tags D4: each Project's tags, so an agent knows what tag_change can use.
        var ids = projects.ConvertAll(p => Guid.Parse(p.Id));
        var tags = (await db.ProjectTags.AsNoTracking()
                .Where(t => ids.Contains(t.ProjectId))
                .Select(t => new { t.ProjectId, t.Key, t.Value, t.KeyKey, t.ValueKey })
                .ToListAsync(cancellationToken))
            .InTagOrder(t => t.KeyKey, t => t.ValueKey)
            .ToLookup(t => t.ProjectId, t => new Tag(t.Key, t.Value).Text);
        projects = projects.ConvertAll(p => p with { Tags = [.. tags[Guid.Parse(p.Id)]] });

        var result = new ListProjectsResult(projects);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Describe() }],
            StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.ListProjectsResult),
        };
    }
}
