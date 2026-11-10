using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Progress;
using Panopticon.Server.Projects;

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
        RequestContext<CallToolRequestParams> context,
        [Description("Required. The repo: host, owner (may contain '/') and name.")] RepoRef? repo = null,
        [Description("Required. The OpenSpec change name.")] string? change = null,
        [Description("Required. The reporting agent: MCP client name and machine name.")] AgentRef? agent = null,
        [Description("Required. The content of openspec/changes/<change>/tasks.md.")] string? tasksMarkdown = null,
        [Description("Optional. The section number the agent is working on.")] int? section = null,
        [Description("Optional. The change folder's Markdown and YAML files: each a path relative to the folder, with '/' separators, and its content. Include tasks.md.")] IReadOnlyList<ChangeDocument>? documents = null,
        [Description("Optional. The archive folder's name, e.g. 2026-11-08-team-auth, when the change has been archived.")] string? archive = null,
        [Description("Optional. The id of the Project this work is linked to, from the repo's .panopticon.json. Without it, the work is the reporter's own.")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var input = Validate(repo, change, agent, tasksMarkdown, section, documents, archive, project);

        // The member comes from the bearer token (design D16): the SDK carries the request's authenticated user into
        // the tool as RequestContext.User. A bootstrap-token report has no member.
        input = input with { Agent = input.Agent with { MemberId = context.User?.GetMemberId() } };

        RecordedReport recorded;
        try
        {
            recorded = await recorder.RecordAsync(input, cancellationToken);
        }
        catch (TasksDocumentTooLargeException ex)
        {
            throw new McpException(ex.Message, ex);
        }
        catch (ReportRefusedException ex)
        {
            throw new McpException(ex.Message, ex);
        }

        // projects D4: a personal change retired by linking its repo leaves its owner's dashboard.
        if (recorded.RetiredChangeId is { } retired)
        {
            broadcaster.PublishArchived(retired, new ChangeScope(null, input.Agent.MemberId));
        }

        // D7: an archived report removes the card; a repeat of it (AlreadyArchived) changed nothing, so says nothing.
        switch (recorded.Archive)
        {
            case ArchiveOutcome.Archived:
                broadcaster.PublishArchived(recorded.ChangeId, recorded.Scope);
                break;
            case ArchiveOutcome.None when await cards.GetAsync(recorded.ChangeId, cancellationToken) is { } card:
                broadcaster.Publish(card, recorded.Scope);
                break;
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

    /// <summary>
    /// Rejects missing or blank fields, naming each one, and trims what it keeps. Then checks the documents and the
    /// archive folder (<see cref="DocumentChecks"/>).
    /// </summary>
    internal static ReportInput Validate(
        RepoRef? repo,
        string? change,
        AgentRef? agent,
        string? tasksMarkdown,
        int? section,
        IReadOnlyList<ChangeDocument?>? documents = null,
        string? archive = null,
        string? project = null)
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

        var changeName = change!.Trim();
        var checkedDocuments = DocumentChecks.Check(documents, tasksMarkdown!);
        if (archive is not null)
        {
            DocumentChecks.CheckArchive(archive, changeName);
        }

        // projects D4: a malformed id is refused the same way as one that isn't the reporter's.
        Guid? projectId = null;
        if (project is not null)
        {
            projectId = Guid.TryParse(project.Trim(), out var parsed)
                ? parsed
                : throw new McpException(ReportRefusedException.NotYourProject(project.Trim()));
        }

        return new ReportInput(
            new RepoIdentity(repo!.Host.Trim(), repo.Owner.Trim(), repo.Name.Trim()),
            changeName,
            new AgentIdentity(agent!.Client.Trim(), agent.Machine.Trim()),
            section,
            tasksMarkdown!)
        {
            Documents = checkedDocuments,
            Archive = archive,
            ProjectId = projectId,
        };
    }

    private static void Require(string? value, string name, List<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(name);
        }
    }
}
