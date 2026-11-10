using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Cli.Git;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// The bridge's local <c>report_progress</c>, <c>list_projects</c> and <c>tag_change</c> tools (spec cli-bridge, "Stdio
/// MCP server").
/// </summary>
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

    /// <summary>
    /// The server's <c>tag_change</c>, forwarded with the same server and token as reports (tags D8). A Project id or
    /// repo the agent leaves out comes from <c>.panopticon.json</c> and the git remote, as for reports.
    /// </summary>
    [McpServerTool(Name = ContractsAssembly.TagChangeToolName, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Put a Project's tags on one of this repo's changes, or take them off. Only the Project's existing tags can be used: list_projects lists them. The bridge finds the Project from .panopticon.json and the repo from git. Needs an owner's or editor's token. Returns the change's project tags afterwards.")]
    public static async Task<CallToolResult> TagChangeAsync(
        BridgeSettings settings,
        ServerForwarder forwarder,
        [Description("The OpenSpec change name: its folder under openspec/changes/ (or its archive).")] string change,
        [Description("The tags to put on the change, as list_projects lists them, e.g. \"bug\" or \"area: onboarding\".")] IReadOnlyList<string>? add = null,
        [Description("The tags to take off the change.")] IReadOnlyList<string>? remove = null,
        [Description("Optional. The Project's id; by default, the one in this repo's .panopticon.json.")] string? project = null,
        [Description("Optional. The change's repo (host, owner, name); by default, this repo's git remote.")] RepoRef? repo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(forwarder);
        try
        {
            // Configuration first, as for reports, so a misconfigured bridge says so before touching the working copy.
            var (endpoint, token) = settings.RequireServer();
            if (project is null || repo is null)
            {
                var root = await GitRepository.FindRootAsync(settings.WorkingDirectory, cancellationToken);
                repo ??= ProgressReporter.ParseRemote(await GitRepository.GetOriginUrlAsync(root, cancellationToken));
                project ??= (await ProjectLink.ReadAsync(root, cancellationToken))?.ToString()
                    ?? throw new BridgeException(
                        $"This repo isn't linked to a Project (it has no {ProjectLink.FileName}), so its changes have no project tags to set. Call list_projects, and link the repo first.");
            }

            var result = await forwarder.TagChangeAsync(
                endpoint,
                token,
                new TagChangeArguments(change, add, remove, project, repo),
                settings.TokenFromEnvironment,
                cancellationToken);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.Describe() }],
                StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.TagChangeResult),
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
