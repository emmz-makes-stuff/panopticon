namespace Panopticon.Server.Data;

// Design D6. Identity is case-insensitive: each identity column has a lower-cased *Key twin
// carrying the unique index, while the display value keeps the casing first reported.

public sealed class Repo
{
    public Guid Id { get; set; }
    public required string Host { get; set; }
    public required string Owner { get; set; }
    public required string Name { get; set; }
    public required string HostKey { get; set; }
    public required string OwnerKey { get; set; }
    public required string NameKey { get; set; }

    public List<Change> Changes { get; } = [];
}

public sealed class Change
{
    public Guid Id { get; set; }
    public Guid RepoId { get; set; }
    public Repo Repo { get; set; } = null!;
    public required string Name { get; set; }
    public required string NameKey { get; set; }
    public required ProgressSnapshot Snapshot { get; set; }
    public DateTimeOffset LastReportedAt { get; set; }

    /// <summary>When the change was archived; null while it is active (change-documents-and-history D5).</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>The archive folder's name, e.g. <c>2026-11-08-team-auth</c>; set together with <see cref="ArchivedAt"/>.</summary>
    public string? ArchiveFolder { get; set; }

    public List<ChangeAgent> Agents { get; } = [];

    // projects D1: a change's scope. A Project's change has ProjectId; a personal one has OwnerMemberId; neither is a
    // Development bootstrap report with no Project, or an old row with no member. Never both (a check constraint).

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? OwnerMemberId { get; set; }
}

/// <summary>A Project (projects D1): the work and people it groups. Its code is fixed once created.</summary>
public sealed class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Upper-case, e.g. <c>PAN</c>; <see cref="CodeKey"/> is its lower-case twin carrying the unique index.</summary>
    public required string Code { get; set; }

    public required string CodeKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByMemberId { get; set; }

    public List<ProjectMember> Members { get; } = [];
}

/// <summary>A member's role in a Project (spec projects, Membership roles).</summary>
public sealed class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;

    /// <summary>One of <see cref="ProjectRoles"/>.</summary>
    public required string Role { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
    public Guid? InvitedByMemberId { get; set; }
}

/// <summary>The roles a Project member can have, as stored.</summary>
public static class ProjectRoles
{
    public const string Owner = "owner";
    public const string Editor = "editor";
    public const string Viewer = "viewer";

    public static readonly IReadOnlyList<string> All = [Owner, Editor, Viewer];
}

/// <summary>A member's MCP client on a machine. No member: a Development bootstrap report, or one from before team-auth.</summary>
public sealed class Agent
{
    public Guid Id { get; set; }
    public Guid? MemberId { get; set; }
    public Member? Member { get; set; }
    public required string ClientName { get; set; }
    public required string MachineName { get; set; }
    public required string ClientNameKey { get; set; }
    public required string MachineNameKey { get; set; }
}

/// <summary>An agent working on a change, and when it last reported on it.</summary>
public sealed class ChangeAgent
{
    public Guid ChangeId { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public DateTimeOffset LastReportedAt { get; set; }
}

/// <summary>Every accepted report; history and metrics build on these (spec: Document retention).</summary>
public sealed class ReportEvent
{
    public long Id { get; set; }
    public Guid ChangeId { get; set; }
    public Guid AgentId { get; set; }
    public int? ReportedSection { get; set; }

    /// <summary>Hex SHA-256 of the reported <c>tasks.md</c>; the object is <c>documents/sha256/&lt;hex&gt;</c>.</summary>
    public required string TasksSha256 { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    // change-metrics D1: the progress this report saw. All null until filled in, by the recorder for a new report or by
    // ProgressBackfill for an older one. OverallTotal = -1 (Unreadable) marks one whose tasks.md couldn't be read.

    public int? OverallDone { get; set; }
    public int? OverallTotal { get; set; }

    /// <summary>The current section's number, when that section is numbered.</summary>
    public int? CurrentSection { get; set; }

    /// <summary>The numbered sections' counts, in document order.</summary>
    public List<SectionCounts>? Sections { get; set; }

    /// <summary>The <see cref="OverallTotal"/> that marks a report whose <c>tasks.md</c> couldn't be read.</summary>
    public const int Unreadable = -1;
}

/// <summary>One numbered <c>tasks.md</c> section as a report saw it (change-metrics D1).</summary>
public sealed class SectionCounts
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
}

/// <summary>One line of a report's manifest: a synced document's path and the hash of the content it carried.</summary>
public sealed class ReportDocument
{
    public long ReportEventId { get; set; }

    /// <summary>Relative to the change's folder, with '/' separators.</summary>
    public required string Path { get; set; }

    /// <summary>Hex SHA-256 of the content; the object is <c>documents/sha256/&lt;hex&gt;</c>.</summary>
    public required string Sha256 { get; set; }

    public int SizeBytes { get; set; }
}

/// <summary>A change's latest synced document, as the change page lists it; replaced whole by each newer report.</summary>
public sealed class LatestDocument
{
    public Guid ChangeId { get; set; }

    /// <summary>Relative to the change's folder, with '/' separators.</summary>
    public required string Path { get; set; }

    /// <summary>Hex SHA-256 of the content; the object is <c>documents/sha256/&lt;hex&gt;</c>.</summary>
    public required string Sha256 { get; set; }

    public int SizeBytes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The latest computed progress of a change, stored as <c>jsonb</c>.</summary>
public sealed class ProgressSnapshot
{
    public int OverallDone { get; set; }
    public int OverallTotal { get; set; }
    public int? CurrentSectionNumber { get; set; }
    public string? CurrentSectionTitle { get; set; }
    public int CurrentSectionDone { get; set; }
    public int CurrentSectionTotal { get; set; }
    public int? ReportedSection { get; set; }
}
