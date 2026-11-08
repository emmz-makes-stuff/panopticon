using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Contracts;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Progress;

namespace Panopticon.Server.Reporting;

/// <summary>The online MCP endpoint's <c>report_progress</c> tool (spec: progress-reporting).</summary>
[McpServerToolType]
public sealed class ReportProgressTool
{
    // Argument parameter names are the wire names in ReportProgressArguments.Names. They all default to
    // null so the SDK binds a call that omits them, and Validate can name every missing field itself
    // (the SDK does not validate arguments; a parameter without a default is rejected before the tool runs).
    [McpServerTool(Name = ContractsAssembly.ReportProgressToolName, Destructive = false, Idempotent = false)]
    [Description("Report progress on an OpenSpec change: which repo and change, the reporting agent, and the change's tasks.md. Returns the computed overall and current-section progress.")]
    public static async Task<CallToolResult> ReportProgressAsync(
        ReportRecorder recorder,
        ChangeCards cards,
        ChangeBroadcaster broadcaster,
        [Description("Required. The repo: host, owner (may contain '/') and name.")] RepoRef? repo = null,
        [Description("Required. The OpenSpec change name.")] string? change = null,
        [Description("Required. The reporting agent: MCP client name and machine name.")] AgentRef? agent = null,
        [Description("Required. The content of openspec/changes/<change>/tasks.md.")] string? tasksMarkdown = null,
        [Description("Optional. The section number the agent is working on.")] int? section = null,
        CancellationToken cancellationToken = default)
    {
        var input = Validate(repo, change, agent, tasksMarkdown, section);

        RecordedReport recorded;
        try
        {
            recorded = await recorder.RecordAsync(input, cancellationToken);
        }
        catch (TasksDocumentTooLargeException ex)
        {
            throw new McpException(ex.Message, ex);
        }

        if (await cards.GetAsync(recorded.ChangeId, cancellationToken) is { } card)
        {
            broadcaster.Publish(card);
        }

        var snapshot = recorded.Snapshot;
        var result = new ReportProgressResult(
            recorded.ChangeId.ToString(),
            new ProgressCount(snapshot.OverallDone, snapshot.OverallTotal),
            snapshot.CurrentSectionNumber is { } number
                ? new SectionSummary(
                    number,
                    snapshot.CurrentSectionTitle ?? string.Empty,
                    new ProgressCount(snapshot.CurrentSectionDone, snapshot.CurrentSectionTotal))
                : null);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Describe() }],
            StructuredContent = JsonSerializer.SerializeToElement(result, ContractsJsonContext.Default.ReportProgressResult),
        };
    }

    /// <summary>Rejects missing or blank fields, naming each one, and trims what it keeps.</summary>
    internal static ReportInput Validate(RepoRef? repo, string? change, AgentRef? agent, string? tasksMarkdown, int? section)
    {
        var missing = new List<string>();
        Require(repo?.Host, "repo.host", missing);
        Require(repo?.Owner, "repo.owner", missing);
        Require(repo?.Name, "repo.name", missing);
        Require(change, "change", missing);
        Require(agent?.Client, "agent.client", missing);
        Require(agent?.Machine, "agent.machine", missing);
        if (tasksMarkdown is null)
        {
            missing.Add("tasksMarkdown");
        }

        if (missing.Count > 0)
        {
            throw new McpException($"Missing required field(s): {string.Join(", ", missing)}.");
        }

        return new ReportInput(
            new RepoIdentity(repo!.Host.Trim(), repo.Owner.Trim(), repo.Name.Trim()),
            change!.Trim(),
            new AgentIdentity(agent!.Client.Trim(), agent.Machine.Trim()),
            section,
            tasksMarkdown!);
    }

    private static void Require(string? value, string name, List<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(name);
        }
    }
}
