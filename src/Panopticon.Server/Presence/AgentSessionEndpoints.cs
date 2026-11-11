using System.Net;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Presence;

/// <summary>
/// The hook endpoints for session presence (agent-interaction D3, D4), for reporters by bearer token. The member is
/// the token's, never the event's (spec agent-presence, Session identity).
/// </summary>
public static class AgentSessionEndpoints
{
    public const string EventsPath = "/" + AgentApiPaths.SessionEvents;

    /// <summary>The longest session id and machine name accepted.</summary>
    public const int MaxSessionIdLength = 200;

    public const int MaxMachineLength = 100;

    public static IEndpointRouteBuilder MapAgentSessions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(EventsPath, RecordAsync).RequireAuthorization(AuthPolicies.Reporter);
        return endpoints;
    }

    /// <summary>
    /// 204 whether or not a card shows the session: an event for a change with no active card is accepted and shows
    /// nowhere. 400 for a malformed event. A bootstrap token has no member to own a session, so its events are dropped.
    /// </summary>
    internal static async Task<IResult> RecordAsync(
        SessionEventRequest request,
        HttpContext context,
        PanopticonDb db,
        PresenceStore presence,
        CancellationToken cancellationToken)
    {
        if (Problem(request) is { } problem)
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, problem);
        }

        if (context.User.GetMemberId() is not { } memberId)
        {
            return TypedResults.NoContent();
        }

        if (request.State == SessionState.Ended)
        {
            presence.End(memberId, request.SessionId, request.OccurredAt);
            return TypedResults.NoContent();
        }

        var memberName = await db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => m.DisplayName)
            .SingleAsync(cancellationToken);
        var placement = await SessionChanges.FindAsync(db, memberId, request.Repo, request.Change, request.Project, cancellationToken);
        presence.Record(memberId, memberName, request.SessionId, placement, request.Machine.Trim(), request.State, request.Activity, request.OccurredAt);
        return TypedResults.NoContent();
    }

    /// <summary>Why the event is malformed, or null.</summary>
    internal static string? Problem(SessionEventRequest request)
    {
        if (SessionFieldsProblem(request.Repo, request.Change, request.Machine, request.SessionId) is { } problem)
        {
            return problem;
        }

        if (request.Activity is { } activity
            && (activity.Length > SessionLimits.MaxActivityLength || activity.Any(char.IsControl)))
        {
            return $"An activity is one line of at most {SessionLimits.MaxActivityLength} characters.";
        }

        return null;
    }

    /// <summary>Why the fields naming a session are malformed, or null: shared by every hook endpoint.</summary>
    internal static string? SessionFieldsProblem(RepoRef? repo, string? change, string? machine, string? sessionId)
    {
        if (repo is null
            || string.IsNullOrWhiteSpace(repo.Host)
            || string.IsNullOrWhiteSpace(repo.Owner)
            || string.IsNullOrWhiteSpace(repo.Name))
        {
            return "Give the repo's host, owner and name.";
        }

        if (string.IsNullOrWhiteSpace(change))
        {
            return "Give the change's name.";
        }

        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > MaxSessionIdLength)
        {
            return $"Give a session id of at most {MaxSessionIdLength} characters.";
        }

        if (string.IsNullOrWhiteSpace(machine) || machine.Length > MaxMachineLength)
        {
            return $"Give a machine name of at most {MaxMachineLength} characters.";
        }

        return null;
    }
}

/// <summary>Which active change a session's events name (D4).</summary>
public static class SessionChanges
{
    /// <summary>
    /// The active change matched as a report would match it: by repo and name, in the Project named when the member
    /// belongs to it, and otherwise in the member's personal scope. Null when there's no such card, including for a
    /// Project that isn't theirs (a report would be refused; a session just shows nowhere).
    /// </summary>
    public static async Task<SessionPlacement?> FindAsync(
        PanopticonDb db,
        Guid memberId,
        RepoRef repo,
        string change,
        string? project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(change);
        ChangeScope scope;
        if (project is null)
        {
            scope = new ChangeScope(null, memberId);
        }
        else if (Guid.TryParse(project.Trim(), out var projectId)
            && await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.MemberId == memberId, cancellationToken))
        {
            scope = new ChangeScope(projectId, null);
        }
        else
        {
            return null;
        }

        var (hostKey, ownerKey, repoKey, changeKey) =
            (IdentityKey.Of(repo.Host), IdentityKey.Of(repo.Owner), IdentityKey.Of(repo.Name), IdentityKey.Of(change));
        var changeId = await db.Changes.AsNoTracking()
            .Where(c => c.ArchivedAt == null
                && c.NameKey == changeKey
                && c.Repo.HostKey == hostKey
                && c.Repo.OwnerKey == ownerKey
                && c.Repo.NameKey == repoKey
                && c.ProjectId == scope.ProjectId
                && c.OwnerMemberId == scope.OwnerMemberId)
            .Select(c => (Guid?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return changeId is { } id ? new SessionPlacement(id, scope) : null;
    }
}
