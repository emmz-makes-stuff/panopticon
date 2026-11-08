namespace Panopticon.Contracts;

// The device-style login `panopticon login` runs against the server (team-auth design D12, D15). The server and the
// CLI share these records; the CLI (Native AOT) serializes them through ContractsJsonContext.

/// <summary>Body of <c>POST /api/device/start</c>: the machine the login is for, shown to the approving member.</summary>
public sealed record DeviceStartRequest(string? MachineName);

/// <summary>
/// Answer to <c>POST /api/device/start</c>. <see cref="DeviceSecret"/> stays with the CLI; a person types
/// <see cref="UserCode"/> (<c>XXXX-XXXX</c>) or opens <see cref="ApproveUrl"/>.
/// </summary>
public sealed record DeviceStartResponse(
    string DeviceSecret,
    string UserCode,
    string ApproveUrl,
    int IntervalSeconds,
    int ExpiresInSeconds);

/// <summary>Body of <c>POST /api/device/poll</c>.</summary>
public sealed record DevicePollRequest(string? DeviceSecret);

/// <summary>The member a device login was approved for.</summary>
public sealed record DeviceMember(string DisplayName, string Email);

/// <summary>
/// Answer to <c>POST /api/device/poll</c>. <see cref="Token"/> and <see cref="Member"/> are set only when
/// <see cref="Status"/> is <see cref="DevicePollStatus.Approved"/>, and that answer is given once.
/// </summary>
public sealed record DevicePollResponse(string Status, string? Token = null, DeviceMember? Member = null);

/// <summary>The values of <see cref="DevicePollResponse.Status"/>.</summary>
public static class DevicePollStatus
{
    public const string Pending = "pending";
    public const string SlowDown = "slow_down";
    public const string Denied = "denied";
    public const string Expired = "expired";
    public const string Approved = "approved";
}
