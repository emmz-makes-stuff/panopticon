using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>The bridge's local <c>report_progress</c> and <c>list_projects</c> tools (spec cli-bridge, "Stdio MCP server").</summary>
[McpServerToolType]
internal sealed class BridgeTools
{
    [McpServerTool(Name = ContractsAssembly.ReportProgressToolName, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Report progress on an OpenSpec change in this repo to the Panopticon dashboard. The bridge reads the change's folder (its Markdown and YAML files, tasks.md included) and the repo identity itself, and notices when the change has been archived. Returns the computed overall and current-section progress.")]
    public static async Task<CallToolResult> ReportProgressAsync(
        McpServer server,
        ProgressReporter reporter,
        [Description("The OpenSpec change name: its folder under openspec/changes/.")] string change,
        [Description("Optional. The number of the tasks.md section (## N.) you are working on.")] int? section = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await reporter.ReportAsync(server.ClientInfo?.Name, change, section, cancellationToken);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.Describe() }],
                StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.ReportProgressResult),
            };
        }
        catch (BridgeException exception)
        {
            return ToolError(exception);
        }
    }

    /// <summary>The server's <c>list_projects</c>, forwarded with the same server and token as reports (projects D6).</summary>
    [McpServerTool(Name = ContractsAssembly.ListProjectsToolName, Destructive = false, Idempotent = true, ReadOnly = true, OpenWorld = true)]
    [Description("List the Panopticon Projects you can report into: each one's id, name, code and your role. To link this repo's work to one, write {\"project\": \"<id>\"} to .panopticon.json at the repo root, and commit it.")]
    public static async Task<CallToolResult> ListProjectsAsync(
        BridgeSettings settings,
        ServerForwarder forwarder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(forwarder);
        try
        {
            var (endpoint, token) = settings.RequireServer();
            var result = await forwarder.ListProjectsAsync(endpoint, token, settings.TokenFromEnvironment, cancellationToken);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.Describe() }],
                StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.ListProjectsResult),
            };
        }
        catch (BridgeException exception)
        {
            return ToolError(exception);
        }
    }

    // A tool error, not a protocol error: the agent sees the message and the bridge keeps serving.
    private static CallToolResult ToolError(BridgeException exception) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = exception.Message }],
    };
}
