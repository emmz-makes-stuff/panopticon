using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Presence;

/// <summary>
/// Body of <c>POST /api/prompts/{id}/answer</c> (D3): <see cref="Answers"/> for a question, each question's text to
/// its chosen labels joined by <c>", "</c> or the Other text; <see cref="Allow"/> for a permission prompt, with an
/// optional <see cref="Message"/> for a deny; or <see cref="Visited"/> for a visit request (Done or Can't), with an
/// optional note in <see cref="Message"/>.
/// </summary>
public sealed record PromptAnswerRequest(
    IReadOnlyDictionary<string, string>? Answers = null,
    bool? Allow = null,
    string? Message = null,
    bool? Visited = null);

/// <summary>One question and the answer given, as an answer's record keeps it: never a preview.</summary>
public sealed record AnsweredQuestion(string Question, string Answer);

/// <summary>
/// An answer given from the dashboard (D8; spec remote-answers, Answers are attributed), as the change page lists it
/// and <c>answer-recorded</c> sends it. <see cref="Questions"/> goes only to viewers who could have answered (D5).
/// </summary>
public sealed record AnswerEntry(
    Guid Id,
    Guid ChangeId,
    string SessionMember,
    string AnsweredBy,
    DateTimeOffset AnsweredAt,
    PromptKind Kind,
    string? Tool = null,
    string? Outcome = null,
    IReadOnlyList<AnsweredQuestion>? Questions = null)
{
    /// <summary>The entry as <paramref name="viewer"/> may see it.</summary>
    public AnswerEntry For(Viewer viewer, Guid sessionMemberId, ChangeScope scope)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return Questions is null || viewer.CanAnswer(sessionMemberId, scope) ? this : this with { Questions = null };
    }
}

/// <summary>Answering a held prompt from the dashboard (D3, D5, D6, D8), for signed-in members.</summary>
public static class PromptAnswerEndpoints
{
    public static string AnswerPath(Guid promptId) => $"/api/prompts/{promptId}/answer";

    /// <summary>The longest answer text, Other included, and the longest deny message.</summary>
    public const int MaxAnswerLength = 2000;

    public const int MaxMessageLength = 500;

    private static readonly JsonSerializerOptions _questionsJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPromptAnswers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/prompts/{id:guid}/answer", AnswerAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    /// <summary>
    /// 200 with the recorded answer; 400 for an answer that doesn't fit the prompt; 403 when the caller may not answer
    /// (checked against the database, never a stream's view); 404 when the prompt is gone; 409 when it's already
    /// answered, being answered, or released. Exactly one of two answers at once wins.
    /// </summary>
    internal static async Task<IResult> AnswerAsync(
        Guid id,
        PromptAnswerRequest request,
        Viewer viewer,
        PanopticonDb db,
        PresenceStore presence,
        ChangeBroadcaster broadcaster,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (presence.Find(id) is not { } prompt)
        {
            return TypedResults.NotFound();
        }

        if (!prompt.Answerable || prompt.Placement is not { } placement)
        {
            return AlreadySettled();
        }

        if (Problem(prompt, request) is { } problem)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, problem);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var change = await db.Changes.AsNoTracking()
            .Where(c => c.Id == placement.ChangeId && c.ArchivedAt == null)
            .Select(c => new { c.ProjectId, c.OwnerMemberId })
            .SingleOrDefaultAsync(cancellationToken);
        if (change is null)
        {
            return TypedResults.NotFound();
        }

        var scope = new ChangeScope(change.ProjectId, change.OwnerMemberId);
        var mayAnswer = viewer.MemberId == prompt.SessionMemberId
            || (scope.ProjectId is { } projectId
                && await db.ProjectMembers.AnyAsync(
                    m => m.ProjectId == projectId && m.MemberId == viewer.MemberId && m.Role == ProjectRoles.Owner,
                    cancellationToken));
        if (!mayAnswer)
        {
            return SignInEndpoints.Problem(HttpStatusCode.Forbidden, "Only the session's member and its Project's owners can answer this.");
        }

        var names = await db.Members.AsNoTracking()
            .Where(m => m.Id == viewer.MemberId || m.Id == prompt.SessionMemberId)
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);
        var questions = prompt.Kind == PromptKind.Question
            ? prompt.Questions!.Select(q => new AnsweredQuestion(q.Question, request.Answers![q.Question].Trim())).ToList()
            : null;
        var outcome = prompt.Kind == PromptKind.Permission ? (request.Allow!.Value ? RemoteAnswer.Allow : RemoteAnswer.Deny) : null;
        var row = new RemoteAnswer
        {
            Id = Guid.CreateVersion7(),
            ChangeId = placement.ChangeId,
            SessionMemberId = prompt.SessionMemberId,
            AnsweredByMemberId = viewer.MemberId,
            AnsweredAt = clock.GetUtcNow(),
            Kind = prompt.Kind == PromptKind.Question ? RemoteAnswer.QuestionKind : RemoteAnswer.PermissionKind,
            ToolName = prompt.Kind == PromptKind.Permission ? prompt.Tool : null,
            Outcome = outcome,
            QuestionsJson = questions is null ? null : JsonSerializer.Serialize(questions, _questionsJson),
        };
        db.RemoteAnswers.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        if (!presence.TryClaim(id))
        {
            return AlreadySettled();
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            presence.Unclaim(id);
            throw;
        }

        var answeredBy = names[viewer.MemberId];
        presence.CompleteAnswer(id, new PromptAnswer(
            answeredBy,
            questions?.ToDictionary(q => q.Question, q => q.Answer),
            request.Allow is { } allow && prompt.Kind == PromptKind.Permission ? allow : null,
            outcome == RemoteAnswer.Deny ? request.Message?.Trim() is { Length: > 0 } message ? message : null : null));

        var entry = new AnswerEntry(
            row.Id,
            row.ChangeId,
            names[prompt.SessionMemberId],
            answeredBy,
            row.AnsweredAt,
            prompt.Kind,
            row.ToolName,
            row.Outcome,
            questions);
        broadcaster.PublishAnswerRecorded(scope, prompt.SessionMemberId, entry);
        return TypedResults.Ok(entry);
    }

    /// <summary>Why the answer doesn't fit the prompt, or null.</summary>
    internal static string? Problem(PromptSnapshot prompt, PromptAnswerRequest request)
    {
        if (prompt.Kind == PromptKind.Permission)
        {
            if (request.Allow is null || request.Answers is not null)
            {
                return "Answer a permission prompt with allow true or false.";
            }

            return request.Message is { Length: > MaxMessageLength } ? $"A message is at most {MaxMessageLength} characters." : null;
        }

        if (request.Answers is not { } answers || request.Allow is not null || request.Message is not null)
        {
            return "Answer a question with an answer for each of its questions.";
        }

        var asked = prompt.Questions!.Select(q => q.Question).ToHashSet(StringComparer.Ordinal);
        if (!asked.SetEquals(answers.Keys))
        {
            return "Give one answer for each question asked, by its text.";
        }

        return answers.Values.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > MaxAnswerLength)
            ? $"Each answer is between 1 and {MaxAnswerLength} characters."
            : null;
    }

    private static IResult AlreadySettled() =>
        SignInEndpoints.Problem(HttpStatusCode.Conflict, "This prompt has already been answered, or has gone back to the terminal.");
}
