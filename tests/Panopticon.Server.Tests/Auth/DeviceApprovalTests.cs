using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/reporter-credentials: "Approving a device login" (server side; design D12, D7).
public class DeviceApprovalTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(ManualClock clock) =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null) { Clock = clock };

    private static Task<HttpResponseMessage> DecideAsync(HttpClient http, string code, string decision) =>
        http.PostApiAsync($"{DeviceEndpoints.LoginPath(code)}/{decision}", null, Ct);

    [Fact]
    public async Task Looking_up_a_code_shows_the_machine_and_approves_nothing()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var cli = factory.CreateClient();
        var start = await DeviceLoginTests.StartAsync(cli);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        var view = await browser.GetFromJsonAsync<DeviceLoginView>(DeviceEndpoints.LoginPath(start.UserCode), Ct);

        Assert.Equal(new DeviceLoginView(start.UserCode, "vm-1", DeviceLoginStates.Pending), view);
        clock.Advance(DeviceEndpoints.PollInterval);
        Assert.Equal(DevicePollStatus.Pending, (await DeviceLoginTests.PollAsync(cli, start.DeviceSecret)).Status);
    }

    // "Approve" (server side): the waiting CLI gets a token for the approving member.
    [Fact]
    public async Task Approve_gives_the_waiting_cli_a_token_for_the_approving_member()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var cli = factory.CreateClient();
        var start = await DeviceLoginTests.StartAsync(cli);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        // Codes match ignoring case and the hyphen.
        using var approved = await DecideAsync(browser, start.UserCode.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant(), "approve");

        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        var view = await browser.GetFromJsonAsync<DeviceLoginView>(DeviceEndpoints.LoginPath(start.UserCode), Ct);
        Assert.Equal(DeviceLoginStates.Approved, view!.State);
        var poll = await DeviceLoginTests.PollAsync(cli, start.DeviceSecret);
        Assert.Equal(DevicePollStatus.Approved, poll.Status);
        Assert.Equal(PanopticonFactory.MemberEmail, poll.Member!.Email);
    }

    // "Deny" (server side).
    [Fact]
    public async Task Deny_tells_the_cli_and_issues_no_token()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var cli = factory.CreateClient();
        var start = await DeviceLoginTests.StartAsync(cli);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var denied = await DecideAsync(browser, start.UserCode, "deny");

        Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);
        Assert.Equal(DevicePollStatus.Denied, (await DeviceLoginTests.PollAsync(cli, start.DeviceSecret)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await DecideAsync(browser, start.UserCode, "approve")).StatusCode);
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.ApiTokens.CountAsync(Ct));
    }

    // "Unknown or expired code".
    [Fact]
    public async Task Unknown_expired_or_used_codes_are_not_valid_and_nothing_is_approved()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var cli = factory.CreateClient();
        var used = await DeviceLoginTests.StartAsync(cli);
        var expiring = await DeviceLoginTests.StartAsync(cli);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await DecideAsync(browser, used.UserCode, "approve")).Dispose();

        using var again = await DecideAsync(browser, used.UserCode, "approve");
        using var unknown = await DecideAsync(browser, "BBBB-BBBB", "approve");
        using var malformed = await DecideAsync(browser, "not-a-code", "approve");
        using var unknownLookup = await browser.GetAsync(DeviceEndpoints.LoginPath("BBBB-BBBB"), Ct);
        clock.Advance(DeviceEndpoints.Lifetime);
        using var expired = await DecideAsync(browser, expiring.UserCode, "approve");

        Assert.All([again, unknown, malformed, unknownLookup, expired], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Contains("isn't valid", (await expired.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        var view = await browser.GetFromJsonAsync<DeviceLoginView>(DeviceEndpoints.LoginPath(expiring.UserCode), Ct);
        Assert.Equal(DeviceLoginStates.Expired, view!.State);
        await using var db = factory.NewDb();
        Assert.Null((await db.DeviceLogins.SingleAsync(l => l.UserCode == Secrets.NormalizeUserCode(expiring.UserCode), Ct)).ApprovedAt);
    }

    [Fact]
    public async Task Code_attempts_are_limited_to_10_a_minute_per_session()
    {
        await using var factory = await FactoryAsync(ManualClock.StartingAt2026());
        using var cli = factory.CreateClient();
        var start = await DeviceLoginTests.StartAsync(cli);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        for (var i = 0; i < 10; i++)
        {
            (await DecideAsync(browser, "BBBB-BBBB", "approve")).Dispose();
        }

        using var eleventh = await DecideAsync(browser, start.UserCode, "approve");

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        await using var db = factory.NewDb();
        Assert.Null((await db.DeviceLogins.SingleAsync(Ct)).ApprovedAt);
    }

    [Fact]
    public async Task Approval_needs_a_session_and_the_request_header()
    {
        await using var factory = await FactoryAsync(ManualClock.StartingAt2026());
        using var cli = factory.CreateClient();
        var start = await DeviceLoginTests.StartAsync(cli);
        using var anonymous = factory.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(DeviceEndpoints.LoginPath(start.UserCode), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DecideAsync(anonymous, start.UserCode, "approve")).StatusCode);

        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        using var noHeader = await browser.PostAsync($"{DeviceEndpoints.LoginPath(start.UserCode)}/approve", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        await using var db = factory.NewDb();
        Assert.Null((await db.DeviceLogins.SingleAsync(Ct)).ApprovedAt);
    }
}
