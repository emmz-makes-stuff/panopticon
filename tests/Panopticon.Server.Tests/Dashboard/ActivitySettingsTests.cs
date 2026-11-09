using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Dashboard;

// specs/live-dashboard: "Quiet thresholds are settings" (filters-and-idle-cards D1), served by GET /api/settings.
public class ActivitySettingsTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(Dictionary<string, string> settings) =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct)) { Settings = settings };

    private static async Task<(long Idle, long Stale)> SettingsAsync(PanopticonFactory factory)
    {
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var settings = await http.GetFromJsonAsync<JsonElement>(DashboardEndpoints.SettingsPath, Ct);
        return (settings.GetProperty("idleAfterSeconds").GetInt64(), settings.GetProperty("staleAfterSeconds").GetInt64());
    }

    // "Default thresholds".
    [Fact]
    public async Task Unset_thresholds_are_30_minutes_and_4_hours()
    {
        await using var factory = await FactoryAsync([]);

        Assert.Equal((1800L, 14400L), await SettingsAsync(factory));
    }

    [Fact]
    public async Task Set_thresholds_are_served_in_seconds()
    {
        await using var factory = await FactoryAsync(new()
        {
            [ActivityOptions.IdleAfterKey] = "00:01:00",
            [ActivityOptions.StaleAfterKey] = "00:05:00",
        });

        Assert.Equal((60L, 300L), await SettingsAsync(factory));
    }

    // "Thresholds out of order".
    [Theory]
    [InlineData("02:00:00", "01:00:00")]
    [InlineData("01:00:00", "01:00:00")]
    public async Task Stale_not_longer_than_idle_fails_at_startup_naming_both(string idle, string stale)
    {
        await using var factory = await FactoryAsync(new()
        {
            [ActivityOptions.IdleAfterKey] = idle,
            [ActivityOptions.StaleAfterKey] = stale,
        });

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(ActivityOptions.IdleAfterKey, error.Message, StringComparison.Ordinal);
        Assert.Contains(ActivityOptions.StaleAfterKey, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActivityOptions.IdleAfterKey, "00:00:00")]
    [InlineData(ActivityOptions.IdleAfterKey, "-00:10:00")]
    [InlineData(ActivityOptions.StaleAfterKey, "00:00:00")]
    public async Task A_threshold_that_isnt_positive_fails_at_startup_naming_it(string key, string value)
    {
        await using var factory = await FactoryAsync(new() { [key] = value });

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_need_sign_in()
    {
        await using var factory = await FactoryAsync([]);
        using var http = factory.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(DashboardEndpoints.SettingsPath, Ct)).StatusCode);
    }
}
