using System.Text.Json;

namespace Panopticon.Contracts;

// The bridge's request_visit tool and the hook that holds it (visit-requests D1, D2). The hook injects a VisitAnswer
// into the tool's input as panopticon_answer; the tool reads it and returns a RequestVisitResult.

/// <summary>
/// The dashboard's answer to a visit request, as the hook injects it into <c>request_visit</c>'s input under
/// <see cref="RequestVisit.AnswerField"/>. <see cref="Outcome"/> is <see cref="RequestVisit.Done"/> or
/// <see cref="RequestVisit.Cant"/>.
/// </summary>
public sealed record VisitAnswer(string Outcome, string? Note, string AnsweredBy);

/// <summary>
/// What <c>request_visit</c> returns (spec cli-bridge, Requesting a visit). <see cref="Reached"/> is false when the
/// dashboard gave no answer, and the agent should ask in the terminal.
/// </summary>
public sealed record RequestVisitResult(
    bool Reached,
    string Url,
    string? Outcome = null,
    string? Note = null,
    string? AnsweredBy = null)
{
    /// <summary>The text the agent reads.</summary>
    public string Describe()
    {
        if (!Reached)
        {
            return "Your member wasn't reached through Panopticon: they aren't away, or the request couldn't be held. "
                + $"Ask them in the terminal instead, and put the URL in the question: {Url}";
        }

        var said = Outcome == RequestVisit.Done ? "Done" : "Can't";
        var note = Note is { Length: > 0 } text ? $": {text}" : "";
        return $"{said}{note}. Answered by {AnsweredBy} via Panopticon.";
    }
}

/// <summary>The names <c>request_visit</c> and its hook share.</summary>
public static class RequestVisit
{
    /// <summary>The input property the hook injects the answer under. The tool doesn't advertise it.</summary>
    public const string AnswerField = "panopticon_answer";

    public const string Done = "done";

    public const string Cant = "cant";

    /// <summary>
    /// The answer in a <c>request_visit</c> call's <paramref name="arguments"/>, or null when there isn't a
    /// well-formed one. A malformed one counts as no answer, so the agent is told to ask in the terminal.
    /// </summary>
    public static VisitAnswer? AnswerIn(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null
            || !arguments.TryGetValue(AnswerField, out var answer)
            || answer.ValueKind != JsonValueKind.Object
            || Text(answer, "outcome") is not ({ } outcome and (Done or Cant))
            || Text(answer, "answeredBy") is not { Length: > 0 } answeredBy)
        {
            return null;
        }

        return new VisitAnswer(outcome, Text(answer, "note"), answeredBy);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
