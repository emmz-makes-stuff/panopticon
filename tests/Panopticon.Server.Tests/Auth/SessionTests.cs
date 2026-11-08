using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/dashboard-sign-in: "Sign-in links", "Confirming before signing in", "Sessions", "Signing out",
// "Cross-site requests refused" (design D4, D6, D8).
public class SessionTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Member = PanopticonFactory.MemberEmail;

    private async Task<PanopticonFactory> FactoryAsync(ManualClock? clock = null, string environment = "Development") =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null)
        {
            Clock = clock,
            Environment = environment,
        };

    private static async Task<string> RequestSecretAsync(PanopticonFactory factory, HttpClient http)
    {
        var before = factory.Emails.To(Member).Count;
        (await http.PostApiAsync(SignInEndpoints.SignInPath, new { email = Member }, Ct)).Dispose();
        await factory.Emails.WaitForAsync(Member, before + 1, Ct);
        return factory.Emails.LatestSignInSecret(Member);
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient http, string secret) =>
        http.PostApiAsync(SignInEndpoints.ConfirmPath, new { secret }, Ct);

    private static async Task<string?> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).TryGetProperty("reason", out var reason) ? reason.GetString() : null;

    [Fact]
    public async Task Confirming_a_link_signs_in_and_the_session_names_the_member()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);

        using var confirmed = await ConfirmAsync(http, await RequestSecretAsync(factory, http));

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var session = await http.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct);
        Assert.Equal(Member, session.GetProperty("email").GetString());
        Assert.Equal("member", session.GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", true)]
    public async Task The_session_cookie_is_http_only_lax_and_secure_outside_development(string environment, bool secure)
    {
        await using var factory = await FactoryAsync(environment: environment);
        using var http = factory.CreateBrowserClient();

        using var confirmed = await ConfirmAsync(http, await RequestSecretAsync(factory, http));

        var cookie = Assert.Single(confirmed.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{SessionStore.CookieName}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(secure, cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Link_used_twice_is_refused_as_used()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();
        var secret = await RequestSecretAsync(factory, http);
        (await ConfirmAsync(http, secret)).Dispose();

        using var again = await ConfirmAsync(factory.CreateBrowserClient(), secret);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(SignInEndpoints.LinkRefusal.Used, await ReasonAsync(again));
        Assert.Contains("already been used", await again.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_link_is_refused_as_expired()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateBrowserClient();
        var secret = await RequestSecretAsync(factory, http);

        clock.Advance(TimeSpan.FromMinutes(16));
        using var late = await ConfirmAsync(http, secret);

        Assert.Equal(SignInEndpoints.LinkRefusal.Expired, await ReasonAsync(late));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_newer_link_leaves_older_unexpired_ones_working()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();
        var older = await RequestSecretAsync(factory, http);
        await RequestSecretAsync(factory, http);

        using var confirmed = await ConfirmAsync(http, older);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [Fact]
    public async Task Link_for_a_removed_member_is_refused()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();
        var secret = await RequestSecretAsync(factory, http);
        await using (var db = factory.NewDb())
        {
            await db.Members.ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, DateTimeOffset.UtcNow), Ct);
        }

        using var refused = await ConfirmAsync(http, secret);

        Assert.Equal(SignInEndpoints.LinkRefusal.Invalid, await ReasonAsync(refused));
    }

    [Theory]
    [InlineData("not-a-real-secret")]
    [InlineData("")]
    public async Task Unknown_link_is_refused_as_invalid(string secret)
    {
        await using var factory = await FactoryAsync();

        using var refused = await ConfirmAsync(factory.CreateBrowserClient(), secret);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(SignInEndpoints.LinkRefusal.Invalid, await ReasonAsync(refused));
    }

    [Fact]
    public async Task Scanner_opening_the_link_spends_nothing()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();
        var secret = await RequestSecretAsync(factory, http);
        var link = factory.Emails.To(Member)[^1].Text.Split('\n').Single(l => l.Contains("sign-in/confirm#", StringComparison.Ordinal)).Trim();

        // What a scanner can do: GET the URL (the fragment is never sent) and the confirm endpoint itself.
        (await http.GetAsync(new Uri(link).PathAndQuery, Ct)).Dispose();
        (await http.GetAsync(SignInEndpoints.ConfirmPath + "?secret=" + secret, Ct)).Dispose();

        await using (var db = factory.NewDb())
        {
            Assert.Null((await db.SignInLinks.SingleAsync(Ct)).UsedAt);
        }

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(http, secret)).StatusCode);
    }

    [Fact]
    public async Task Active_use_slides_the_session_and_stays_signed_in()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateBrowserClient();
        (await ConfirmAsync(http, await RequestSecretAsync(factory, http))).Dispose();

        for (var day = 0; day < 10; day++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
        }

        await using var db = factory.NewDb();
        var session = await db.Sessions.SingleAsync(Ct);
        Assert.Equal(clock.GetUtcNow() + SessionStore.Lifetime, session.ExpiresAt);
    }

    [Fact]
    public async Task Idle_for_more_than_a_week_asks_to_sign_in_again()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateBrowserClient();
        (await ConfirmAsync(http, await RequestSecretAsync(factory, http))).Dispose();

        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
    }

    [Fact]
    public async Task Sliding_writes_at_most_once_a_minute()
    {
        var clock = ManualClock.StartingAt2026();
        await using var factory = await FactoryAsync(clock);
        using var http = factory.CreateBrowserClient();
        (await ConfirmAsync(http, await RequestSecretAsync(factory, http))).Dispose();

        clock.Advance(TimeSpan.FromSeconds(30));
        (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).Dispose();

        await using var db = factory.NewDb();
        var session = await db.Sessions.SingleAsync(Ct);
        Assert.Equal(session.CreatedAt, session.LastSeenAt);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_so_a_copy_of_the_cookie_is_refused()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();
        using var confirmed = await ConfirmAsync(http, await RequestSecretAsync(factory, http));
        var cookie = confirmed.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var signedOut = await http.PostApiAsync(SessionEndpoints.SignOutPath, null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
        using var copy = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost/"), HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, SessionEndpoints.SessionPath);
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.SendAsync(request, Ct)).StatusCode);
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.Sessions.CountAsync(Ct));
    }

    [Fact]
    public async Task Forged_request_without_the_dashboard_header_is_refused()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        // What a cross-site form post can send: the cookie, but no custom header.
        using var forged = await http.PostAsync(SessionEndpoints.SignOutPath, new FormUrlEncodedContent([]), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
    }

    [Fact]
    public async Task Sign_in_and_confirm_need_the_header_too()
    {
        await using var factory = await FactoryAsync();
        using var http = factory.CreateBrowserClient();

        using var signIn = await http.PostAsJsonAsync(SignInEndpoints.SignInPath, new { email = Member }, Ct);
        using var confirm = await http.PostAsJsonAsync(SignInEndpoints.ConfirmPath, new { secret = "x" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
    }
}
