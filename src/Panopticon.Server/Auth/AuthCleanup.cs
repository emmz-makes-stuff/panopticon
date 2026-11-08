using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>
/// Deletes auth rows nobody can use any more (team-auth D19): sessions, sign-in links and device logins that expired
/// more than <see cref="Retention"/> ago. A spent sign-in link still has its expiry, so it goes the same way. It runs
/// once soon after start-up, then every <see cref="Interval"/>. API tokens and members are never purged: they're the
/// record of who reported.
/// </summary>
public sealed partial class AuthCleanup(IServiceScopeFactory scopes, TimeProvider time, ILogger<AuthCleanup> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    /// <summary>Deletes what has expired; returns how many rows went.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PanopticonDb>();
        var before = time.GetUtcNow() - Retention;
        var sessions = await db.Sessions.Where(s => s.ExpiresAt < before).ExecuteDeleteAsync(cancellationToken);
        var links = await db.SignInLinks.Where(l => l.ExpiresAt < before).ExecuteDeleteAsync(cancellationToken);
        var logins = await db.DeviceLogins.Where(l => l.ExpiresAt < before).ExecuteDeleteAsync(cancellationToken);
        var total = sessions + links + logins;
        if (total > 0)
        {
            LogPurged(sessions, links, logins);
        }

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Try again next time: a missed purge only leaves dead rows a little longer.
                LogPurgeFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged expired auth rows: {Sessions} sessions, {Links} sign-in links, {Logins} device logins")]
    private partial void LogPurged(int sessions, int links, int logins);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purging expired auth rows failed")]
    private partial void LogPurgeFailed(Exception exception);
}
