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
    [Description("List the Projects you can report into: each one's id, name, code and your role. Put a Project's id in the repo's .panopticon.json, as {\"project\": \"<id>\"}, to link the repo's work to it.")]
    public static async Task<CallToolResult> ListProjectsAsync(
        PanopticonDb db,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A member's token lists their own Projects; the Development bootstrap token, with no member, lists every
        // Project as a viewer, since it may report into any of them (D4).
        var projects = context.User?.GetMemberId() is { } memberId
            ? await db.Projects.AsNoTracking()
                .SelectMany(p => p.Members.Where(m => m.MemberId == memberId), (p, m) => new { p, m.Role })
                .OrderBy(x => x.p.CodeKey)
                .Select(x => new ProjectSummary(x.p.Id.ToString(), x.p.Name, x.p.Code, x.Role))
                .ToListAsync(cancellationToken)
            : await db.Projects.AsNoTracking()
                .OrderBy(p => p.CodeKey)
                .Select(p => new ProjectSummary(p.Id.ToString(), p.Name, p.Code, ProjectRoles.Viewer))
                .ToListAsync(cancellationToken);

        var result = new ListProjectsResult(projects);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Describe() }],
            StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.ListProjectsResult),
        };
    }
}
