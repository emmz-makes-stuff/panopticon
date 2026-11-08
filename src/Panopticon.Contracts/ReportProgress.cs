using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Contracts;

// Wire contract for the server's `report_progress` MCP tool (design D15). The server binds these as
// tool arguments; the AOT CLI builds and reads them through ContractsJsonContext (source-generated).

/// <summary>Where the change lives. Owner may contain '/', e.g. GitLab subgroups or Azure DevOps org/project.</summary>
public sealed record RepoRef(string Host, string Owner, string Name);

/// <summary>The reporting agent: its MCP client name, and the machine it runs on.</summary>
public sealed record AgentRef(string Client, string Machine);

/// <summary>Arguments of the server's <c>report_progress</c> tool.</summary>
public sealed record ReportProgressArguments(
    RepoRef Repo,
    string Change,
    AgentRef Agent,
    string TasksMarkdown,
    int? Section = null)
{
    /// <summary>The tool's argument names, as they appear on the wire.</summary>
    public static class Names
    {
        public const string Repo = "repo";
        public const string Change = "change";
        public const string Agent = "agent";
        public const string TasksMarkdown = "tasksMarkdown";
        public const string Section = "section";
    }

    /// <summary>The argument dictionary an MCP client sends, built without reflection (AOT-safe).</summary>
    public Dictionary<string, object?> ToToolArguments()
    {
        var context = ContractsJsonContext.Default;
        var arguments = new Dictionary<string, object?>
        {
            [Names.Repo] = JsonSerializer.SerializeToElement(Repo, context.RepoRef),
            [Names.Change] = JsonSerializer.SerializeToElement(Change, context.String),
            [Names.Agent] = JsonSerializer.SerializeToElement(Agent, context.AgentRef),
            [Names.TasksMarkdown] = JsonSerializer.SerializeToElement(TasksMarkdown, context.String),
        };

        if (Section is { } section)
        {
            arguments[Names.Section] = JsonSerializer.SerializeToElement(section, context.Int32);
        }

        return arguments;
    }
}

/// <summary>Done over total tasks.</summary>
public sealed record ProgressCount(int Done, int Total);

/// <summary>A section of the change's tasks.md and its progress.</summary>
public sealed record SectionSummary(int Number, string Title, ProgressCount Progress);

/// <summary>The structured result of <c>report_progress</c>: the change's newly computed progress.</summary>
public sealed record ReportProgressResult(
    string ChangeId,
    ProgressCount Overall,
    SectionSummary? CurrentSection)
{
    /// <summary>A one-line, human-readable summary, e.g. "Overall 7/20 · section 2 Core 2/5".</summary>
    public string Describe() => CurrentSection is { } section
        ? $"Overall {Overall.Done}/{Overall.Total} · section {section.Number} {section.Title} {section.Progress.Done}/{section.Progress.Total}"
        : $"Overall {Overall.Done}/{Overall.Total}";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RepoRef))]
[JsonSerializable(typeof(AgentRef))]
[JsonSerializable(typeof(ReportProgressArguments))]
[JsonSerializable(typeof(ReportProgressResult))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
