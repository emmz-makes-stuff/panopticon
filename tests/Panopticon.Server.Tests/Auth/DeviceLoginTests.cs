using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/reporter-credentials: "Starting a device login", "Device login secrets" (design D12, D7).
public class DeviceLoginTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(ManualClock clock) =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null)
        {
            Clock = clock,
            Settings = new Dictionary<string, string> { [PublicUrlOptions.ConfigKey] = PanopticonFactory.PublicUrl },
        };

    internal static async Task<DeviceStartResponse> StartAsync(HttpClient http, string machineName = "vm-1")
    {
        using var response = await http.PostApiAsync(DeviceEndpoints.StartPath, new { machineName }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceStartResponse>(Ct))!;
    }

    internal static async Task<DevicePollResponse> PollAsync(HttpClient http, string deviceSecret)
    {
        using var response = await http.PostApiAsync(DeviceEndpoints.PollPath, new { deviceSecret }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DevicePollResponse>(Ct))!;
    }

    /// <summary>Approves directly in the database; the approval endpoints are covered in DeviceApprovalTests.</summary>
    private static async Task ApproveInDbAsync(PanopticonFactory factory, string userCode, DateTimeOffset at)
    {
        await using var db = factory.NewDb();
        var memberId = await db.Members.Where(m => m.EmailKey == PanopticonFactory.MemberEmail).Select(m => m.Id).SingleAsync(Ct);
        var code = Secrets.NormalizeUserCode(userCode);
        await db.DeviceLogins.Where(l => l.UserCode == code).ExecuteUpdateAsync(
            l => l.SetProperty(x => x.ApprovedByMemberId, memberId).SetProperty(x => x.ApprovedAt, at),
            Ct);
    }

    [Fact]
    public async Task Start_returns_a_code_the_approval_url_and_the_timings()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();

        var start = await StartAsync(http, "  vm-1 ");

        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$", start.UserCode);
        Assert.Equal($"{PanopticonFactory.PublicUrl}device?code={start.UserCode}", start.ApproveUrl);
        Assert.Equal(5, start.IntervalSeconds);
        Assert.Equal(600, start.ExpiresInSeconds);
        await using var db = factory.NewDb();
        var row = await db.DeviceLogins.SingleAsync(Ct);
        Assert.Equal("vm-1", row.MachineName);
        Assert.Equal(Secrets.Hash(start.DeviceSecret), row.DeviceSecretHash);
        Assert.Equal(clock.GetUtcNow() + DeviceEndpoints.Lifetime, row.ExpiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Start_needs_a_machine_name(string? machineName)
    {
        await using var factory = await FactoryAsync(ManualClock.StartingAt2026());
        using var http = factory.CreateClient();

        using var response = await http.PostApiAsync(DeviceEndpoints.StartPath, new { machineName }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_refuses_a_machine_name_over_100_characters()
    {
        await using var factory = await FactoryAsync(ManualClock.StartingAt2026());
        using var http = factory.CreateClient();

        using var tooLong = await http.PostApiAsync(DeviceEndpoints.StartPath, new { machineName = new string('m', 101) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.NotNull(await StartAsync(http, new string('m', 100)));
    }

    [Fact]
    public async Task Start_is_limited_to_10_per_client()
    {
        await using var factory = await FactoryAsync(ManualClock.StartingAt2026());
        using var http = factory.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            await StartAsync(http);
        }

        using var eleventh = await http.PostApiAsync(DeviceEndpoints.StartPath, new { machineName = "vm-1" }, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    [Fact]
    public async Task Poll_is_pending_until_approved()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);

        Assert.Equal(DevicePollStatus.Pending, (await PollAsync(http, start.DeviceSecret)).Status);
        clock.Advance(DeviceEndpoints.PollInterval);
        var again = await PollAsync(http, start.DeviceSecret);

        Assert.Equal(new DevicePollResponse(DevicePollStatus.Pending), again);
    }

    // "Nobody approves" (server side).
    [Fact]
    public async Task Nobody_approves_and_the_login_expires_after_ten_minutes()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);

        clock.Advance(DeviceEndpoints.Lifetime);

        Assert.Equal(DevicePollStatus.Expired, (await PollAsync(http, start.DeviceSecret)).Status);
        await ApproveInDbAsync(factory, start.UserCode, clock.GetUtcNow());
        clock.Advance(DeviceEndpoints.PollInterval);
        Assert.Equal(DevicePollStatus.Expired, (await PollAsync(http, start.DeviceSecret)).Status);
    }

    // "Token handed over once".
    [Fact]
    public async Task Approved_hands_the_token_over_once_then_expired()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);
        await ApproveInDbAsync(factory, start.UserCode, clock.GetUtcNow());

        var approved = await PollAsync(http, start.DeviceSecret);

        Assert.Equal(DevicePollStatus.Approved, approved.Status);
        Assert.StartsWith(Secrets.ApiTokenPrefix, approved.Token, StringComparison.Ordinal);
        Assert.Equal(new DeviceMember("member", PanopticonFactory.MemberEmail), approved.Member);
        await using (var db = factory.NewDb())
        {
            var token = await db.ApiTokens.Include(t => t.Member).SingleAsync(Ct);
            Assert.Equal(Secrets.Hash(approved.Token!), token.SecretHash);
            Assert.Equal("vm-1", token.MachineName);
            Assert.Equal(PanopticonFactory.MemberEmail, token.Member.EmailKey);
            var login = await db.DeviceLogins.SingleAsync(Ct);
            Assert.Equal(token.Id, login.TokenId);
            Assert.NotNull(login.TokenDeliveredAt);
        }

        clock.Advance(DeviceEndpoints.PollInterval);
        var again = await PollAsync(http, start.DeviceSecret);

        Assert.Equal(new DevicePollResponse(DevicePollStatus.Expired), again);
        await using var after = factory.NewDb();
        Assert.Equal(1, await after.ApiTokens.CountAsync(Ct));
    }

    [Fact]
    public async Task Concurrent_polls_after_approval_issue_one_token()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);
        await ApproveInDbAsync(factory, start.UserCode, clock.GetUtcNow());

        var answers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PollAsync(http, start.DeviceSecret)));

        Assert.Single(answers, a => a.Status == DevicePollStatus.Approved);
        await using var db = factory.NewDb();
        Assert.Equal(1, await db.ApiTokens.CountAsync(Ct));
    }

    [Fact]
    public async Task Denied_is_reported_and_no_token_is_issued()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);
        await using (var db = factory.NewDb())
        {
            await db.DeviceLogins.ExecuteUpdateAsync(l => l.SetProperty(x => x.DeniedAt, clock.GetUtcNow()), Ct);
        }

        Assert.Equal(new DevicePollResponse(DevicePollStatus.Denied), await PollAsync(http, start.DeviceSecret));
        await using var after = factory.NewDb();
        Assert.Equal(0, await after.ApiTokens.CountAsync(Ct));
    }

    // "Polling too fast".
    [Fact]
    public async Task Polling_too_fast_is_told_to_slow_down_and_the_login_is_unaffected()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);
        Assert.Equal(DevicePollStatus.Pending, (await PollAsync(http, start.DeviceSecret)).Status);

        clock.Advance(TimeSpan.FromSeconds(2));
        await ApproveInDbAsync(factory, start.UserCode, clock.GetUtcNow());

        Assert.Equal(new DevicePollResponse(DevicePollStatus.SlowDown), await PollAsync(http, start.DeviceSecret));
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(DevicePollStatus.Approved, (await PollAsync(http, start.DeviceSecret)).Status);
    }

    [Fact]
    public async Task An_unknown_device_secret_reads_as_expired_and_a_missing_one_is_a_400()
    {
        await using var factory = await FactoryAsync(ManualClock.StartingAt2026());
        using var http = factory.CreateClient();

        Assert.Equal(DevicePollStatus.Expired, (await PollAsync(http, "not-a-secret")).Status);
        using var missing = await http.PostApiAsync(DeviceEndpoints.PollPath, new { }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task An_approver_removed_before_the_poll_gets_no_token()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateClient();
        var start = await StartAsync(http);
        await ApproveInDbAsync(factory, start.UserCode, clock.GetUtcNow());
        await using (var db = factory.NewDb())
        {
            await db.Members.ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, clock.GetUtcNow()), Ct);
        }

        Assert.Equal(DevicePollStatus.Expired, (await PollAsync(http, start.DeviceSecret)).Status);
        await using var after = factory.NewDb();
        Assert.Equal(0, await after.ApiTokens.CountAsync(Ct));
    }
}
