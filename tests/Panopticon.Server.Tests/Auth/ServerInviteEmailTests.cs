using Panopticon.Server.Auth;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/team-members: "Invitation email" and "Invite rate limit" (server-invites D2, D3).
public class ServerInviteEmailTests
{
    private static readonly Uri _signInPage = new("https://panopticon.test/");

    [Fact]
    public void The_email_names_the_inviter_and_links_to_the_sign_in_page()
    {
        var email = ServerInviteEmail.Create("kit@example.com", "Emmz", _signInPage);

        Assert.Equal("kit@example.com", email.To);
        Assert.Equal("Emmz invited you to PANOPTICON", email.Subject);
        Assert.Contains("Emmz invited you", email.Text, StringComparison.Ordinal);
        Assert.Contains("https://panopticon.test/", email.Text, StringComparison.Ordinal);
        Assert.Contains("kit@example.com", email.Text, StringComparison.Ordinal);
        Assert.Contains("Emmz invited you", email.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://panopticon.test/\"", email.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_email_holds_no_sign_in_link()
    {
        var email = ServerInviteEmail.Create("kit@example.com", "Emmz", _signInPage);

        Assert.DoesNotContain("sign-in/confirm", email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sign-in/confirm", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain('#', email.Text);
    }

    [Fact]
    public void Markup_in_the_inviters_name_is_text_in_the_html_body()
    {
        var email = ServerInviteEmail.Create("kit@example.com", "<b>Emmz</b>", _signInPage);

        Assert.Contains("&lt;b&gt;Emmz&lt;/b&gt; invited you", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", email.Html, StringComparison.Ordinal);
        Assert.Contains("<b>Emmz</b> invited you", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_job_sends_the_email_as_it_is()
    {
        var email = ServerInviteEmail.Create("kit@example.com", "Emmz", _signInPage);
        var job = new ServerInviteJob(email);

        Assert.Same(email, await job.PrepareAsync(null!, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("kit", job.Description, StringComparison.Ordinal);
    }

    // "Development without Resend": the invite goes through the same IEmailSender as sign-in links, which is the logging
    // sender in Development with no key (EmailSetupTests).
    [Fact]
    public async Task In_development_the_logging_sender_logs_the_invitation()
    {
        var logger = new ListLogger<LoggingEmailSender>();

        await new LoggingEmailSender(logger).SendAsync(
            ServerInviteEmail.Create("kit@example.com", "Emmz", _signInPage),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("Emmz invited you to PANOPTICON", entry.Message, StringComparison.Ordinal);
        Assert.Contains("https://panopticon.test/", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_member_may_invite_twenty_times_an_hour()
    {
        using var limits = new AuthLimits();
        var emmz = Guid.NewGuid();
        var bob = Guid.NewGuid();

        for (var i = 0; i < AuthLimits.ServerInvitesPerHour; i++)
        {
            Assert.True(limits.TryServerInvite(emmz));
        }

        Assert.False(limits.TryServerInvite(emmz));
        Assert.True(limits.TryServerInvite(bob));
    }
}
