using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Contracts;

// Wire contract for the server's `report_progress` MCP tool (design D15). The server binds these as
// tool arguments; the AOT CLI builds and reads them through ContractsJsonContext (source-generated).

/// <summary>Where the change lives. Owner may contain '/', e.g. GitLab subgroups or Azure DevOps org/project.</summary>
public sealed record RepoRef(string Host, string Owner, string Name);

/// <summary>The reporting agent: its MCP client name, and the machine it runs on.</summary>
public sealed record AgentRef(string Client, string Machine);

/// <summary>One synced file of the change's folder: its path relative to that folder, with '/' separators.</summary>
public sealed record ChangeDocument(string Path, string Content);

/// <summary>Arguments of the server's <c>report_progress</c> tool.</summary>
/// <remarks>
/// <see cref="Documents"/> and <see cref="Archive"/> are optional (change-documents-and-history D1): a CLI from before
/// them leaves them out, and an older server, which binds only its named parameters, ignores them.
/// <see cref="Project"/> (projects D10) is optional the same way: the id from the repo's <c>.panopticon.json</c>, as text.
/// <see cref="PersonalTags"/> (tags D5) too: null leaves the agent's personal tags as they are, and an empty list clears them.
/// </remarks>
public sealed record ReportProgressArguments(
    RepoRef Repo,
    string Change,
    AgentRef Agent,
    string TasksMarkdown,
    int? Section = null,
    IReadOnlyList<ChangeDocument>? Documents = null,
    string? Archive = null,
    string? Project = null,
    IReadOnlyList<string>? PersonalTags = null)
{
    /// <summary>The tool's argument names, as they appear on the wire.</summary>
    public static class Names
    {
        public const string Repo = "repo";
        public const string Change = "change";
        public const string Agent = "agent";
        public const string TasksMarkdown = "tasksMarkdown";
        public const string Section = "section";
        public const string Documents = "documents";
        public const string Archive = "archive";
        public const string Project = "project";
        public const string PersonalTags = "personalTags";
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

        if (Documents is { } documents)
        {
            arguments[Names.Documents] = JsonSerializer.SerializeToElement(documents, context.IReadOnlyListChangeDocument);
        }

        if (Archive is { } archive)
        {
            arguments[Names.Archive] = JsonSerializer.SerializeToElement(archive, context.String);
        }

        if (Project is { } project)
        {
            arguments[Names.Project] = JsonSerializer.SerializeToElement(project, context.String);
        }

        if (PersonalTags is { } personalTags)
        {
            arguments[Names.PersonalTags] = JsonSerializer.SerializeToElement(personalTags, context.IReadOnlyListString);
        }

        return arguments;
    }
}

/// <summary>Done over total tasks.</summary>
public sealed record ProgressCount(int Done, int Total);

/// <summary>A section of the change's tasks.md and its progress.</summary>
public sealed record SectionSummary(int Number, string Title, ProgressCount Progress);

/// <summary>The structured result of <c>report_progress</c>: the change's newly computed progress.</summary>
/// <param name="Archived">
/// The archive folder's name when the report was of an archived change. The bridge sets it for its own reply (spec
/// cli-bridge, Archived change reported); the server leaves it out.
/// </param>
public sealed record ReportProgressResult(
    string ChangeId,
    ProgressCount Overall,
    SectionSummary? CurrentSection,
    string? Archived = null)
{
    /// <summary>
    /// A one-line, human-readable summary, e.g. "Overall 7/20 · section 2 Core 2/5", or for an archived change
    /// "Overall 20/20 · archived as 2026-11-08-team-auth".
    /// </summary>
    public string Describe()
    {
        var progress = CurrentSection is { } section
            ? $"Overall {Overall.Done}/{Overall.Total} · section {section.Number} {section.Title} {section.Progress.Done}/{section.Progress.Total}"
            : $"Overall {Overall.Done}/{Overall.Total}";
        return Archived is { } folder ? $"{progress} · archived as {folder}" : progress;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RepoRef))]
[JsonSerializable(typeof(AgentRef))]
[JsonSerializable(typeof(ChangeDocument))]
[JsonSerializable(typeof(IReadOnlyList<ChangeDocument>))]
[JsonSerializable(typeof(ReportProgressArguments))]
[JsonSerializable(typeof(ReportProgressResult))]
[JsonSerializable(typeof(ListProjectsResult))]
[JsonSerializable(typeof(TagChangeArguments))]
[JsonSerializable(typeof(TagChangeResult))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(DeviceStartRequest))]
[JsonSerializable(typeof(DeviceStartResponse))]
[JsonSerializable(typeof(DevicePollRequest))]
[JsonSerializable(typeof(DevicePollResponse))]
[JsonSerializable(typeof(CliManifest))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
