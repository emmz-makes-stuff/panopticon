using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panopticon.Server.Auth;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/dashboard-sign-in: "Sign-in email delivery" (start-up validation and which sender is used; design D9).
public class EmailSetupTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(string environment, Dictionary<string, string> settings) =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null)
        {
            Environment = environment,
            Settings = settings,
            CaptureEmail = false,
        };

    [Theory]
    [InlineData("Production", "Panopticon:Resend:ApiKey", "")]
    [InlineData("Production", "Panopticon:Email:From", "")]
    [InlineData("Production", "Panopticon:PublicUrl", "")]
    [InlineData("Testing", "Panopticon:Resend:ApiKey", " ")]
    public async Task Missing_email_setting_outside_development_fails_at_startup_naming_it(string environment, string key, string value)
    {
        await using var factory = await FactoryAsync(environment, new() { [key] = value });

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_with_no_resend_key_logs_emails_instead_of_sending()
    {
        await using var factory = await FactoryAsync("Development", new()
        {
            ["Panopticon:Resend:ApiKey"] = string.Empty,
            ["Panopticon:Email:From"] = string.Empty,
            ["Panopticon:PublicUrl"] = string.Empty,
        });
        factory.CreateClient().Dispose();

        using var scope = factory.Services.CreateScope();
        Assert.IsType<LoggingEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_resend_key_sends_through_resend(string environment)
    {
        await using var factory = await FactoryAsync(environment, []);
        factory.CreateClient().Dispose();

        using var scope = factory.Services.CreateScope();
        Assert.IsType<ResendEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public async Task The_logging_sender_puts_the_whole_email_in_the_log()
    {
        var logger = new ListLogger<LoggingEmailSender>();

        await new LoggingEmailSender(logger).SendAsync(
            new OutgoingEmail("emmz@example.com", "Sign in", "Open http://localhost:7480/sign-in/confirm#abc", "<p>html</p>"),
            Ct);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("/sign-in/confirm#abc", entry.Message, StringComparison.Ordinal);
    }
}
