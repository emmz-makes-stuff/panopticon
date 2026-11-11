using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Contracts;

// A session's question or permission prompt, posted by the sync hook and held while its member is away
// (agent-interaction D3, D6). Through ContractsJsonContext, like every other contract.

/// <summary>What a prompt is: <c>question</c> (<c>AskUserQuestion</c>) or <c>permission</c> (<c>PermissionRequest</c>).</summary>
[JsonConverter(typeof(PromptKindConverter))]
public enum PromptKind
{
    Question,
    Permission,
}

/// <summary>Reads and writes <see cref="PromptKind"/> by kebab-case name, refusing numbers.</summary>
public sealed class PromptKindConverter()
    : JsonStringEnumConverter<PromptKind>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false);

/// <summary>One option of a question, as <c>AskUserQuestion</c> gives it.</summary>
public sealed record PromptOption(string Label, string? Description = null, string? Preview = null);

/// <summary>One question of an <c>AskUserQuestion</c> call (spec remote-answers, Question forms).</summary>
public sealed record PromptQuestion(
    string Question,
    string Header,
    IReadOnlyList<PromptOption> Options,
    bool MultiSelect = false);

/// <summary>
/// Body of <c>POST /api/agent/prompts</c>: the session fields, plus <see cref="Questions"/> for a question or
/// <see cref="Tool"/> for a permission prompt. A permission prompt never carries its input here (D2): that goes to
/// <c>PUT /api/agent/prompts/{id}/preview</c>, and only once the server has held the prompt.
/// </summary>
public sealed record PromptRequest(
    RepoRef Repo,
    string Change,
    string Machine,
    string SessionId,
    PromptKind Kind,
    IReadOnlyList<PromptQuestion>? Questions = null,
    string? Tool = null,
    string? Project = null);

/// <summary>
/// Answer to <c>POST /api/agent/prompts</c>. The server decides <see cref="Held"/> from the member's away flag in this
/// one call (D3); when it's false, the terminal asks as usual.
/// </summary>
public sealed record PromptCreated(string PromptId, bool Held);

/// <summary>Body of <c>PUT /api/agent/prompts/{id}/preview</c>: a held permission prompt's input, cut to 2 KB.</summary>
public sealed record PromptPreview(string Preview);

/// <summary>Where a held prompt stands, as <c>GET /api/agent/prompts/{id}/wait</c> reports it.</summary>
[JsonConverter(typeof(PromptWaitStateConverter))]
public enum PromptWaitState
{
    /// <summary>Still held: call <c>wait</c> again.</summary>
    Pending,

    /// <summary>Answered from the dashboard: <see cref="PromptWaitResult.Answer"/> is set.</summary>
    Answered,

    /// <summary>Given back to the terminal unanswered (away off, 8 hours, or the hook went quiet).</summary>
    Released,
}

/// <summary>Reads and writes <see cref="PromptWaitState"/> by kebab-case name, refusing numbers.</summary>
public sealed class PromptWaitStateConverter()
    : JsonStringEnumConverter<PromptWaitState>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false);

/// <summary>
/// An answer from the dashboard, for the hook's output (D7). A question's has <see cref="Answers"/>: each question's
/// text to its chosen labels joined by <c>", "</c>, or the Other text. A permission prompt's has <see cref="Allow"/>,
/// and for a deny an optional <see cref="Message"/>.
/// </summary>
/// <param name="AnsweredBy">The answering member's display name, for "Answered by … via Panopticon."</param>
public sealed record PromptAnswer(
    string AnsweredBy,
    IReadOnlyDictionary<string, string>? Answers = null,
    bool? Allow = null,
    string? Message = null);

/// <summary>Answer to <c>GET /api/agent/prompts/{id}/wait</c>.</summary>
public sealed record PromptWaitResult(PromptWaitState State, PromptAnswer? Answer = null);

/// <summary>
/// Which prompts may go to the dashboard (spec remote-answers, Prompt limits). The hook checks before posting, and the
/// server checks again, since it is the trust boundary. A prompt over a limit goes to the terminal. Sizes are UTF-8
/// byte counts.
/// </summary>
public static class PromptLimits
{
    public const int MaxQuestions = 4;

    public const int MaxOptions = 4;

    /// <summary>Any one option's preview: 8 KB.</summary>
    public const int MaxPreviewBytes = 8 * 1024;

    /// <summary>The whole prompt request, as JSON: 64 KB.</summary>
    public const int MaxPromptBytes = 64 * 1024;

    /// <summary>A permission prompt's input preview is cut to 2 KB.</summary>
    public const int MaxPermissionPreviewBytes = 2 * 1024;

    /// <summary>Why <paramref name="request"/> can't go to the dashboard, or null when it can.</summary>
    public static string? Problem(PromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Kind)
        {
            case PromptKind.Question:
                if (request.Questions is not { Count: > 0 } questions)
                {
                    return "a question prompt has no questions";
                }

                if (questions.Count > MaxQuestions)
                {
                    return $"it has {questions.Count} questions, over {MaxQuestions}";
                }

                foreach (var question in questions)
                {
                    if (question.Options is not { Count: > 0 } options)
                    {
                        return "a question has no options";
                    }

                    if (options.Count > MaxOptions)
                    {
                        return $"a question has {options.Count} options, over {MaxOptions}";
                    }

                    if (options.Any(option => option.Preview is { } preview && Encoding.UTF8.GetByteCount(preview) > MaxPreviewBytes))
                    {
                        return "a preview is over 8 KB";
                    }
                }

                if (request.Tool is not null)
                {
                    return "a question prompt names a tool";
                }

                break;

            case PromptKind.Permission:
                if (string.IsNullOrWhiteSpace(request.Tool))
                {
                    return "a permission prompt has no tool";
                }

                if (request.Questions is not null)
                {
                    return "a permission prompt has questions";
                }

                break;

            default:
                return "it isn't a question or a permission prompt";
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, ContractsJsonContext.Default.PromptRequest).Length;
        return bytes > MaxPromptBytes ? "it is over 64 KB" : null;
    }

    /// <summary>
    /// <paramref name="input"/> cut to at most <see cref="MaxPermissionPreviewBytes"/> UTF-8 bytes, never splitting a
    /// character.
    /// </summary>
    public static string CutPermissionPreview(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Encoding.UTF8.GetByteCount(input) <= MaxPermissionPreviewBytes)
        {
            return input;
        }

        var bytes = 0;
        var end = 0;
        foreach (var rune in input.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxPermissionPreviewBytes)
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }

        return input[..end];
    }
}
