using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Data;

namespace Panopticon.Server.Changes;

/// <summary>A repo filter's identity keys (lower-cased, as identity is matched).</summary>
public sealed record RepoKey(string Host, string Owner, string Name);

/// <summary>An owner filter's identity keys. <see cref="Owner"/> may contain '/' (a GitLab subgroup) and is matched whole.</summary>
public sealed record OwnerKey(string Host, string Owner);

/// <summary>
/// One chosen tag in the Tags filter (tags D7), from a <c>tag</c> value: <c>t:&lt;tag id&gt;</c>,
/// <c>k:&lt;project id&gt;:&lt;key&gt;</c>, or <c>p:&lt;key&gt;</c> / <c>p:&lt;key&gt;:&lt;value&gt;</c>. Keys and values are
/// held lower-cased, as tags match.
/// </summary>
public abstract record TagFilter
{
    private TagFilter()
    {
    }

    /// <summary>The <c>t:</c> form: one Project tag, by id, so renaming it keeps the filter.</summary>
    public sealed record ProjectTag(Guid TagId) : TagFilter
    {
        public override string ToString() => $"t:{TagId}";
    }

    /// <summary>The <c>k:</c> form: a Project's key alone, with any value.</summary>
    public sealed record ProjectKey(Guid ProjectId, string KeyKey) : TagFilter
    {
        public override string ToString() => $"k:{ProjectId}:{KeyKey}";
    }

    /// <summary>The <c>p:</c> form: the viewer's own personal tag, or with no value, their key alone.</summary>
    public sealed record Personal(string KeyKey, string? ValueKey) : TagFilter
    {
        public override string ToString() => ValueKey is null ? $"p:{KeyKey}" : $"p:{KeyKey}:{ValueKey}";
    }

    public const string Malformed = "tag must be t:<tag id>, k:<project id>:<key>, or p:<key> or p:<key>:<value>.";

    /// <summary>Reads one <c>tag</c> value; on a malformed one, <paramref name="error"/> names the parameter.</summary>
    public static bool TryParse(string value, [NotNullWhen(true)] out TagFilter? filter, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);
        filter = null;
        error = Malformed;
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[1] != ':')
        {
            return false;
        }

        var rest = trimmed[2..];
        switch (trimmed[0])
        {
            case 't' when Guid.TryParse(rest, out var tagId):
                filter = new ProjectTag(tagId);
                break;
            case 'k':
                var colon = rest.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0
                    || !Guid.TryParse(rest[..colon], out var projectId)
                    || !TagText.TryParse(rest[(colon + 1)..], out var key, out _)
                    || key.Value is not null)
                {
                    return false;
                }

                filter = new ProjectKey(projectId, key.KeyKey);
                break;
            case 'p' when TagText.TryParse(rest, out var tag, out _):
                filter = new Personal(tag.KeyKey, tag.Value is null ? null : tag.ValueKey);
                break;
            default:
                return false;
        }

        error = null;
        return true;
    }
}

/// <summary>
/// The history's filters (filters-and-idle-cards D3, D4; spec: change-filters), one value each, all optional. A change
/// matches when it matches every one given: repo and owner on the repo's keys, and member, machine and client when any
/// of its agents has that value. <see cref="Project"/> matches the change's Project, and <see cref="Personal"/> the
/// personal changes; with the viewer's visibility applied too, those are the viewer's own (projects D9).
/// <see cref="Tags"/> is the one filter with many values: a change matches when it has every one (tags D7).
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
    /// <summary>The chosen tags, all of which a change must have.</summary>
    public IReadOnlyList<TagFilter> Tags { get; init; } = [];

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
        IReadOnlyList<string>? tags,
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

        // tags D7: repeated tag values, blank ones ignored, a repeat counted once, and at most MaxFilterTags.
        var tagFilters = new List<TagFilter>();
        foreach (var value in tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!TagFilter.TryParse(value, out var tagFilter, out error))
            {
                return false;
            }

            if (!tagFilters.Contains(tagFilter))
            {
                tagFilters.Add(tagFilter);
            }
        }

        if (tagFilters.Count > TagText.MaxFilterTags)
        {
            error = $"tag can be given at most {TagText.MaxFilterTags} times.";
            return false;
        }

        filter = new HistoryFilter(repoKey, ownerKey, memberId, KeyOrNull(machine), KeyOrNull(client), projectId, personal)
        {
            Tags = tagFilters,
        };
        return true;
    }

    /// <summary>
    /// The changes matching every filter. <paramref name="viewerMemberId"/> is whose agents a personal tag is looked
    /// for on: the viewer's own, and nobody else's (tags D7).
    /// </summary>
    public IQueryable<Change> Apply(IQueryable<Change> changes, Guid viewerMemberId)
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

        // One EXISTS per tag (tags D7).
        foreach (var tag in Tags)
        {
            changes = tag switch
            {
                TagFilter.ProjectTag(var tagId) => changes.Where(c => c.ProjectTags.Any(t => t.TagId == tagId)),
                TagFilter.ProjectKey(var projectId, var key) =>
                    changes.Where(c => c.ProjectTags.Any(t => t.Tag.ProjectId == projectId && t.Tag.KeyKey == key)),
                TagFilter.Personal(var key, null) => changes.Where(c => c.Agents.Any(a =>
                    a.Agent.MemberId == viewerMemberId && a.Tags.Any(t => t.KeyKey == key))),
                TagFilter.Personal(var key, var value) => changes.Where(c => c.Agents.Any(a =>
                    a.Agent.MemberId == viewerMemberId && a.Tags.Any(t => t.KeyKey == key && t.ValueKey == value))),
                _ => throw new System.Diagnostics.UnreachableException(),
            };
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
