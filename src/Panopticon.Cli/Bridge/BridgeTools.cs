using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>The bridge's local <c>report_progress</c> tool (spec cli-bridge, "Stdio MCP server").</summary>
[McpServerToolType]
internal sealed class BridgeTools
{
    [McpServerTool(Name = ContractsAssembly.ReportProgressToolName, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Report progress on an OpenSpec change in this repo to the Panopticon dashboard. The bridge reads the change's tasks.md and the repo identity itself. Returns the computed overall and current-section progress.")]
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
            // A tool error, not a protocol error: the agent sees the message and the bridge keeps serving.
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = exception.Message }],
            };
        }
    }
}
