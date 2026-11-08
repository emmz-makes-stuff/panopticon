using System.Threading.RateLimiting;

namespace Panopticon.Server.Auth;

/// <summary>
/// In-memory rate limits for the open auth endpoints (team-auth D7). One container per deployment, so in-memory is
/// enough; a restart resets them.
/// </summary>
public sealed class AuthLimits : IDisposable
{
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(15);

    private readonly PartitionedRateLimiter<string> _signInPerEmail = Sliding(permits: 5, _window);
    private readonly PartitionedRateLimiter<string> _signInPerClient = Sliding(permits: 20, _window);
    private readonly PartitionedRateLimiter<string> _deviceStartPerClient = Sliding(permits: 10, _window);
    private readonly PartitionedRateLimiter<string> _approvalsPerSession = Sliding(permits: 10, TimeSpan.FromMinutes(1));

    /// <summary>True when a sign-in link may be requested for this email key from this client.</summary>
    public bool TrySignIn(string emailKey, string client) =>
        Acquire(_signInPerClient, client) && Acquire(_signInPerEmail, emailKey);

    public bool TryDeviceStart(string client) => Acquire(_deviceStartPerClient, client);

    public bool TryApprovalAttempt(Guid sessionId) => Acquire(_approvalsPerSession, sessionId.ToString("N"));

    /// <summary>The client's address for limiting; every unknown address shares one bucket.</summary>
    public static string ClientKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public void Dispose()
    {
        _signInPerEmail.Dispose();
        _signInPerClient.Dispose();
        _deviceStartPerClient.Dispose();
        _approvalsPerSession.Dispose();
    }

    private static bool Acquire(PartitionedRateLimiter<string> limiter, string key)
    {
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Sliding(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetSlidingWindowLimiter(
            key,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                SegmentsPerWindow = 3,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}
