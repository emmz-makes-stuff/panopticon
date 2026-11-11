using System.Globalization;
using System.Text;
using System.Text.Json;
using Panopticon.Contracts;

namespace Panopticon.Cli.Hook;

/// <summary>
/// A session's activity as one line of at most <see cref="MaxLength"/> characters (agent-interaction D2; spec
/// agent-presence, Activity text). Made on the machine, so a tool's input never leaves it: the only things taken from
/// the input are Bash's own description, a file's name (never its path or content), an agent's description and a
/// skill's name.
/// </summary>
internal static class ActivitySummary
{
    public const int MaxLength = SessionLimits.MaxActivityLength;

    private const string McpPrefix = "mcp__";

    /// <summary>The line for a tool call; <paramref name="toolInput"/> may be missing or of any shape.</summary>
    public static string For(string toolName, JsonElement? toolInput)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        var summary = toolName switch
        {
            "Bash" => Text(toolInput, "description") ?? "Running a command",
            "Edit" or "MultiEdit" or "Write" => FileName(toolInput, "file_path") is { } file ? $"Editing {file}" : "Editing a file",
            "NotebookEdit" => FileName(toolInput, "notebook_path") is { } notebook ? $"Editing {notebook}" : "Editing a notebook",
            "Read" => FileName(toolInput, "file_path") is { } read ? $"Reading {read}" : "Reading a file",
            "Grep" or "Glob" => "Searching the code",
            "WebFetch" or "WebSearch" => "Searching the web",
            "Agent" or "Task" => Text(toolInput, "description") is { } task ? $"Running agent: {task}" : "Running an agent",
            "Skill" => Text(toolInput, "skill") is { } skill ? $"Using skill {skill}" : "Using a skill",
            _ when Mcp(toolName) is { } mcp => $"Calling {mcp.Server} {mcp.Tool}",
            _ => $"Using {toolName}",
        };
        return OneLine(summary);
    }

    /// <summary>Whitespace and control characters collapsed to single spaces, then cut to <see cref="MaxLength"/>.</summary>
    internal static string OneLine(string text)
    {
        var line = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var blank = char.IsWhiteSpace(c) || char.IsControl(c);
            if (!blank)
            {
                line.Append(c);
            }
            else if (line.Length > 0 && line[^1] != ' ')
            {
                line.Append(' ');
            }
        }

        var trimmed = line.ToString().TrimEnd();
        if (trimmed.Length <= MaxLength)
        {
            return trimmed;
        }

        // Cut on a whole text element, so a surrogate pair or an accent is never split.
        var cut = 0;
        var elements = StringInfo.GetTextElementEnumerator(trimmed);
        while (elements.MoveNext() && elements.ElementIndex + elements.GetTextElement().Length <= MaxLength)
        {
            cut = elements.ElementIndex + elements.GetTextElement().Length;
        }

        return trimmed[..cut].TrimEnd();
    }

    private static string? Text(JsonElement? input, string property) =>
        input is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out var field)
        && field.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(field.GetString())
            ? field.GetString()
            : null;

    // The last segment of either kind of separator, so a Windows path reads right whichever OS reads it.
    private static string? FileName(JsonElement? input, string property) =>
        Text(input, property) is { } path && path.TrimEnd('/', '\\') is { Length: > 0 } trimmed
            ? trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..]
            : null;

    // mcp__<server>__<tool>; the server name may hold single underscores (plugin_panopticon_panopticon).
    private static (string Server, string Tool)? Mcp(string toolName)
    {
        if (!toolName.StartsWith(McpPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = toolName[McpPrefix.Length..];
        var split = rest.IndexOf("__", StringComparison.Ordinal);
        return split > 0 && split + 2 < rest.Length ? (rest[..split], rest[(split + 2)..]) : null;
    }
}
