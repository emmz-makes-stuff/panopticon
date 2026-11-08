namespace Panopticon.Server.Data;

// team-auth design D1. Every secret is stored only as its SHA-256 hash (D3), looked up by that hash.

/// <summary>A team member. A removed member keeps the row, so seeding skips them and past reports keep a name.</summary>
public sealed class Member
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string EmailKey { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? InvitedByMemberId { get; set; }

    public bool IsActive => RemovedAt is null;
}

/// <summary>A magic sign-in link: single use, short-lived.</summary>
public sealed class SignInLink
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;
    public required byte[] SecretHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>A dashboard session, held by an opaque cookie. Deleting the row ends the session.</summary>
public sealed class Session
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;
    public required byte[] SecretHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A device-style CLI login: a code a person approves, and a secret only the CLI holds.</summary>
public sealed class DeviceLogin
{
    public Guid Id { get; set; }
    public required byte[] DeviceSecretHash { get; set; }

    /// <summary>The normalized code (8 letters, no hyphen); unique among codes that are still pending.</summary>
    public required string UserCode { get; set; }

    public required string MachineName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public Guid? ApprovedByMemberId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? DeniedAt { get; set; }
    public Guid? TokenId { get; set; }
    public DateTimeOffset? TokenDeliveredAt { get; set; }
}

/// <summary>A member's API token for one reporter. Valid until revoked or the member is removed.</summary>
public sealed class ApiToken
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;
    public required byte[] SecretHash { get; set; }
    public required string MachineName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
