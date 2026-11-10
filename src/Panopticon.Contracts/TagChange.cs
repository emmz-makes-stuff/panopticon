using System.Text.Json;

namespace Panopticon.Contracts;

/// <summary>
/// Arguments of the <c>tag_change</c> tool (tags D4, D8): put a Project's existing tags on one of its changes, or take
/// them off. Tags are text (<c>bug</c>, <c>area: onboarding</c>), as <c>list_projects</c> lists them.
/// </summary>
/// <param name="Project">The Project's id. The bridge fills it from <c>.panopticon.json</c> when it's left out.</param>
/// <param name="Repo">The change's repo. The bridge fills it from the git remote when it's left out.</param>
public sealed record TagChangeArguments(
    string Change,
    IReadOnlyList<string>? Add = null,
    IReadOnlyList<string>? Remove = null,
    string? Project = null,
    RepoRef? Repo = null)
{
    /// <summary>The tool's argument names, as they appear on the wire.</summary>
    public static class Names
    {
        public const string Change = "change";
        public const string Add = "add";
        public const string Remove = "remove";
        public const string Project = "project";
        public const string Repo = "repo";
    }

    /// <summary>The argument dictionary an MCP client sends, built without reflection (AOT-safe).</summary>
    public Dictionary<string, object?> ToToolArguments()
    {
        var context = ContractsJsonContext.Default;
        var arguments = new Dictionary<string, object?>
        {
            [Names.Change] = JsonSerializer.SerializeToElement(Change, context.String),
            [Names.Add] = JsonSerializer.SerializeToElement(Add ?? [], context.IReadOnlyListString),
            [Names.Remove] = JsonSerializer.SerializeToElement(Remove ?? [], context.IReadOnlyListString),
        };

        if (Project is { } project)
        {
            arguments[Names.Project] = JsonSerializer.SerializeToElement(project, context.String);
        }

        if (Repo is { } repo)
        {
            arguments[Names.Repo] = JsonSerializer.SerializeToElement(repo, context.RepoRef);
        }

        return arguments;
    }
}

/// <summary>The structured result of <c>tag_change</c>: the change's project tags afterwards, in canonical text.</summary>
/// <param name="Code">The Project's code, e.g. <c>PAN</c>.</param>
public sealed record TagChangeResult(string ChangeId, string Change, string Code, IReadOnlyList<string> Tags)
{
    /// <summary>What an agent reads, e.g. "walking-skeleton now has PAN/bug, PAN/area: onboarding."</summary>
    public string Describe() => Tags.Count == 0
        ? $"{Change} now has no {Code} tags."
        : $"{Change} now has {string.Join(", ", Tags.Select(t => $"{Code}/{t}"))}.";
}
