using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;

namespace Panopticon.Server.Changes;

/// <summary>A repo filter's identity keys (lower-cased, as identity is matched).</summary>
public sealed record RepoKey(string Host, string Owner, string Name);

/// <summary>An owner filter's identity keys. <see cref="Owner"/> may contain '/' (a GitLab subgroup) and is matched whole.</summary>
public sealed record OwnerKey(string Host, string Owner);

/// <summary>
/// The history's filters (filters-and-idle-cards D3, D4; spec: change-filters), one value each, all optional. A change
/// matches when it matches every one given: repo and owner on the repo's keys, and member, machine and client when any
/// of its agents has that value. <see cref="Project"/> matches the change's Project, and <see cref="Personal"/> the
/// personal changes; with the viewer's visibility applied too, those are the viewer's own (projects D9).
/// </summary>
public sealed record HistoryFilter(
    RepoKey? Repo,
    OwnerKey? Owner,
    Guid? Member,
    string? Machine,
    string? Client,
    Guid? Project = null,
    bool Personal = false)
{
    /// <summary>The <c>project</c> value that means the personal changes.</summary>
    public const string PersonalValue = "personal";

    /// <summary>
    /// The filter from the query's values; a blank value means no filter. On a malformed value, <paramref name="error"/>
    /// names the parameter and the filter is null.
    /// </summary>
    public static bool TryParse(
        string? repo,
        string? owner,
        string? member,
        string? machine,
        string? client,
        string? project,
        [NotNullWhen(true)] out HistoryFilter? filter,
        [NotNullWhen(false)] out string? error)
    {
        filter = null;
        error = null;

        RepoKey? repoKey = null;
        if (!string.IsNullOrWhiteSpace(repo))
        {
            // host/owner…/name: the first segment is the host, the last the name, and the rest the owner.
            var segments = Segments(repo);
            if (segments is null || segments.Length < 3)
            {
                error = "repo must be host/owner/name, e.g. github.com/emmz/panopticon.";
                return false;
            }

            repoKey = new RepoKey(segments[0], string.Join('/', segments[1..^1]), segments[^1]);
        }

        OwnerKey? ownerKey = null;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            var segments = Segments(owner);
            if (segments is null || segments.Length < 2)
            {
                error = "owner must be host/owner, e.g. github.com/emmz.";
                return false;
            }

            ownerKey = new OwnerKey(segments[0], string.Join('/', segments[1..]));
        }

        Guid? memberId = null;
        if (!string.IsNullOrWhiteSpace(member))
        {
            if (!Guid.TryParse(member.Trim(), out var parsed))
            {
                error = "member must be a member id.";
                return false;
            }

            memberId = parsed;
        }

        // projects D9: a Project id, or "personal".
        Guid? projectId = null;
        var personal = false;
        if (!string.IsNullOrWhiteSpace(project))
        {
            if (string.Equals(project.Trim(), PersonalValue, StringComparison.OrdinalIgnoreCase))
            {
                personal = true;
            }
            else if (Guid.TryParse(project.Trim(), out var parsed))
            {
                projectId = parsed;
            }
            else
            {
                error = $"project must be a Project id or {PersonalValue}.";
                return false;
            }
        }

        filter = new HistoryFilter(repoKey, ownerKey, memberId, KeyOrNull(machine), KeyOrNull(client), projectId, personal);
        return true;
    }

    public IQueryable<Change> Apply(IQueryable<Change> changes)
    {
        if (Project is { } project)
        {
            changes = changes.Where(c => c.ProjectId == project);
        }

        if (Personal)
        {
            changes = changes.Where(c => c.OwnerMemberId != null);
        }

        if (Repo is { } repo)
        {
            changes = changes.Where(c => c.Repo.HostKey == repo.Host && c.Repo.OwnerKey == repo.Owner && c.Repo.NameKey == repo.Name);
        }

        if (Owner is { } owner)
        {
            changes = changes.Where(c => c.Repo.HostKey == owner.Host && c.Repo.OwnerKey == owner.Owner);
        }

        if (Member is { } member)
        {
            changes = changes.Where(c => c.Agents.Any(a => a.Agent.MemberId == member));
        }

        if (Machine is { } machine)
        {
            changes = changes.Where(c => c.Agents.Any(a => a.Agent.MachineNameKey == machine));
        }

        if (Client is { } client)
        {
            changes = changes.Where(c => c.Agents.Any(a => a.Agent.ClientNameKey == client));
        }

        return changes;
    }

    // Lower-cased, trimmed segments; null when any is empty (e.g. "github.com//x" or a trailing '/').
    private static string[]? Segments(string value)
    {
        var segments = value.Trim().Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = IdentityKey.Of(segments[i]);
            if (segments[i].Length == 0)
            {
                return null;
            }
        }

        return segments;
    }

    private static string? KeyOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : IdentityKey.Of(value);
}
