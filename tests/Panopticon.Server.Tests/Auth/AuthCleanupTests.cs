using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// Design D19: expired auth rows are purged a day after they expire. API tokens and members are kept.
public class AuthCleanupTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Purge_deletes_rows_expired_more_than_a_day_ago_and_keeps_the_rest()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Clock = clock,
        };
        factory.CreateClient().Dispose();
        var now = clock.GetUtcNow();
        var longAgo = now - AuthCleanup.Retention - TimeSpan.FromMinutes(1);
        var recently = now - AuthCleanup.Retention + TimeSpan.FromMinutes(1);

        await using (var db = factory.NewDb())
        {
            var member = await db.Members.SingleAsync(Ct);
            foreach (var (name, expires) in new[] { ("old", longAgo), ("recent", recently), ("live", now + TimeSpan.FromDays(1)) })
            {
                db.Sessions.Add(new Session { MemberId = member.Id, SecretHash = Secrets.Hash($"session-{name}"), CreatedAt = expires, LastSeenAt = expires, ExpiresAt = expires });
                db.SignInLinks.Add(new SignInLink { MemberId = member.Id, SecretHash = Secrets.Hash($"link-{name}"), CreatedAt = expires, ExpiresAt = expires, UsedAt = name == "old" ? expires : null });
                db.DeviceLogins.Add(new DeviceLogin { DeviceSecretHash = Secrets.Hash($"device-{name}"), UserCode = Secrets.NewUserCode(), MachineName = name, CreatedAt = expires, ExpiresAt = expires });
            }

            db.ApiTokens.Add(new ApiToken { MemberId = member.Id, SecretHash = Secrets.Hash("token"), MachineName = "vm-1", CreatedAt = longAgo });
            await db.SaveChangesAsync(Ct);
        }

        var purged = await factory.Services.GetRequiredService<AuthCleanup>().PurgeAsync(Ct);

        Assert.Equal(3, purged);
        await using var after = factory.NewDb();
        Assert.Equal(2, await after.Sessions.CountAsync(Ct));
        Assert.Equal(2, await after.SignInLinks.CountAsync(Ct));
        Assert.Equal(["live", "recent"], await after.DeviceLogins.Select(l => l.MachineName).Order().ToListAsync(Ct));
        Assert.Equal(1, await after.ApiTokens.CountAsync(Ct));
        Assert.Equal(1, await after.Members.CountAsync(Ct));
    }
}
