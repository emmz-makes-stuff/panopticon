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

    public List<ChangeAgent> Agents { get; } = [];
}

public sealed class Agent
{
    public Guid Id { get; set; }
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
