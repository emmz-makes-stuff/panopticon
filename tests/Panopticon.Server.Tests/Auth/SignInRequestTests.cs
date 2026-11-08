using System.Net;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/dashboard-sign-in: "Requesting a sign-in link", "Sign-in links" (stored hashed), "Sign-in email delivery"
// (failure) and "Limiting sign-in requests" (design D7, D8, D9).
public class SignInRequestTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Member = PanopticonFactory.MemberEmail;

    private static Task<HttpResponseMessage> RequestLinkAsync(HttpClient http, string email) =>
        http.PostApiAsync(SignInEndpoints.SignInPath, new { email }, Ct);

    [Fact]
    public async Task Member_asks_for_a_link_and_gets_an_email_with_one()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = factory.CreateClient();

        using var response = await RequestLinkAsync(http, " MEMBER@example.com ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("Check your email", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var email = Assert.Single(await factory.Emails.WaitForAsync(Member, cancellationToken: Ct));
        Assert.Equal(SignInEmail.Subject, email.Subject);
        Assert.Contains($"{PanopticonFactory.PublicUrl}sign-in/confirm#", email.Text, StringComparison.Ordinal);
        Assert.Contains("15 minutes", email.Text, StringComparison.Ordinal);
        Assert.Contains("sign-in/confirm#", email.Html, StringComparison.Ordinal);

        // Only the secret's hash is stored.
        var secret = factory.Emails.LatestSignInSecret(Member);
        await using var db = factory.NewDb();
        var link = await db.SignInLinks.SingleAsync(Ct);
        Assert.Equal(Secrets.Hash(secret), link.SecretHash);
        Assert.Equal(SignInEndpoints.LinkLifetime, link.ExpiresAt - link.CreatedAt);
        Assert.Null(link.UsedAt);
    }

    [Fact]
    public async Task Stranger_gets_the_same_answer_and_no_email()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = factory.CreateClient();

        using var stranger = await RequestLinkAsync(http, "stranger@example.com");
        using var member = await RequestLinkAsync(http, Member);

        Assert.Equal(member.StatusCode, stranger.StatusCode);
        Assert.Equal(await member.Content.ReadAsStringAsync(Ct), await stranger.Content.ReadAsStringAsync(Ct));

        // Emails go out in the order they're queued, so once the member's has arrived, any stranger's would have too.
        await factory.Emails.WaitForAsync(Member, cancellationToken: Ct);
        Assert.Empty(factory.Emails.To("stranger@example.com"));
        await using var db = factory.NewDb();
        Assert.Equal(1, await db.SignInLinks.CountAsync(Ct));
    }

    [Fact]
    public async Task Removed_member_gets_no_email()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = factory.CreateClient();
        await using (var db = factory.NewDb())
        {
            await db.Members.ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, DateTimeOffset.UtcNow), Ct);
            db.Members.Add(new Data.Member { Email = "sam@example.com", EmailKey = "sam@example.com", DisplayName = "sam" });
            await db.SaveChangesAsync(Ct);
        }

        using var removed = await RequestLinkAsync(http, Member);
        using var active = await RequestLinkAsync(http, "sam@example.com");

        Assert.Equal(HttpStatusCode.Accepted, removed.StatusCode);
        await factory.Emails.WaitForAsync("sam@example.com", cancellationToken: Ct);
        Assert.Empty(factory.Emails.To(Member));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Invalid_address_is_a_400(string email)
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = factory.CreateClient();

        using var response = await RequestLinkAsync(http, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("valid email address", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delivery_failure_is_logged_and_the_page_still_says_check_your_email()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        factory.Emails.FailWith = new HttpRequestException("Resend is down");
        using var http = factory.CreateClient();

        using var response = await RequestLinkAsync(http, Member);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!factory.Logs.Text.Contains("Sending an email failed", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        Assert.Contains("Sending an email failed", factory.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sign-in/confirm#", factory.Logs.Text, StringComparison.Ordinal);

        // The outbox keeps going after a failure.
        factory.Emails.FailWith = null;
        using var again = await RequestLinkAsync(http, Member);
        await factory.Emails.WaitForAsync(Member, cancellationToken: Ct);
    }

    [Fact]
    public async Task Repeated_requests_for_one_address_are_refused_after_five()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var allowed = await RequestLinkAsync(http, Member);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var refused = await RequestLinkAsync(http, Member.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Try again", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await factory.Emails.WaitForAsync(Member, 5, Ct);
        await using var db = factory.NewDb();
        Assert.Equal(5, await db.SignInLinks.CountAsync(Ct));

        // A stranger's address is limited the same way, so the limit reveals nothing either.
        for (var i = 0; i < 5; i++)
        {
            (await RequestLinkAsync(http, "stranger@example.com")).Dispose();
        }

        using var strangerRefused = await RequestLinkAsync(http, "stranger@example.com");
        Assert.Equal(HttpStatusCode.TooManyRequests, strangerRefused.StatusCode);
    }

    [Fact]
    public async Task One_client_is_refused_after_twenty_requests_across_addresses()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var http = factory.CreateClient();

        for (var i = 0; i < 20; i++)
        {
            using var allowed = await RequestLinkAsync(http, $"person{i}@example.com");
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var refused = await RequestLinkAsync(http, "person-21@example.com");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }
}
