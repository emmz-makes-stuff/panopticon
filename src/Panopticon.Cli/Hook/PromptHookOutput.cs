using System.Text;
using System.Text.Json;
using Panopticon.Contracts;

namespace Panopticon.Cli.Hook;

/// <summary>
/// What the sync hook reads from a prompt's tool input, and the output that answers it (agent-interaction D2, D7).
/// </summary>
internal static class PromptHookOutput
{
    /// <summary>
    /// <c>AskUserQuestion</c>'s questions, as the dashboard shows them, or null when the input isn't that shape: the
    /// terminal then asks as usual.
    /// </summary>
    public static IReadOnlyList<PromptQuestion>? Questions(JsonElement? toolInput)
    {
        if (toolInput is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty("questions", out var questions)
            || questions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<PromptQuestion>();
        foreach (var question in questions.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Object
                || Text(question, "question") is not { } text
                || !question.TryGetProperty("options", out var options)
                || options.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<PromptOption>();
            foreach (var option in options.EnumerateArray())
            {
                if (option.ValueKind != JsonValueKind.Object || Text(option, "label") is not { } label)
                {
                    return null;
                }

                parsed.Add(new PromptOption(label, Text(option, "description"), Text(option, "preview")));
            }

            var multiSelect = question.TryGetProperty("multiSelect", out var multi) && multi.ValueKind == JsonValueKind.True;
            result.Add(new PromptQuestion(text, Text(question, "header") ?? "", parsed, multiSelect));
        }

        return result;
    }

    /// <summary>
    /// A held permission prompt's input as the dashboard previews it, cut to 2 KB: a Bash call's command, or any other
    /// tool's input as JSON. Never sent until the server holds the prompt (spec remote-answers, Permission input stays
    /// local unless held).
    /// </summary>
    public static string Preview(string toolName, JsonElement? toolInput)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        var preview = toolInput switch
        {
            { ValueKind: JsonValueKind.Object } input when toolName == "Bash" && Text(input, "command") is { } command => command,
            { } input => Indented(input),
            null => "",
        };
        return PromptLimits.CutPermissionPreview(preview);
    }

    /// <summary>
    /// D7's output for an answer: a question's answers injected into its input, or a permission's allow or deny. Null
    /// when the answer doesn't fit the prompt (the terminal asks instead).
    /// </summary>
    public static string? For(PromptKind kind, JsonElement? toolInput, PromptAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartObject("hookSpecificOutput");
            if (kind == PromptKind.Question)
            {
                if (answer.Answers is not { } answers)
                {
                    return null;
                }

                json.WriteString("hookEventName", "PreToolUse");
                json.WriteString("permissionDecision", "allow");
                json.WriteStartObject("updatedInput");
                if (toolInput is { ValueKind: JsonValueKind.Object } input)
                {
                    foreach (var property in input.EnumerateObject().Where(p => p.Name != "answers"))
                    {
                        property.WriteTo(json);
                    }
                }

                json.WriteStartObject("answers");
                foreach (var (question, text) in answers)
                {
                    json.WriteString(question, text);
                }

                json.WriteEndObject();
                json.WriteEndObject();
                json.WriteString("additionalContext", $"Answered by {answer.AnsweredBy} via Panopticon.");
            }
            else
            {
                if (answer.Allow is not { } allow)
                {
                    return null;
                }

                json.WriteString("hookEventName", "PermissionRequest");
                json.WriteStartObject("decision");
                json.WriteString("behavior", allow ? "allow" : "deny");
                if (!allow)
                {
                    json.WriteString("message", answer.Message is { Length: > 0 } message
                        ? $"Denied by {answer.AnsweredBy} via Panopticon: {message}"
                        : $"Denied by {answer.AnsweredBy} via Panopticon.");
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Indented(JsonElement input)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            input.WriteTo(json);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
