using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/reporter-credentials: "API tokens", "Tokens page" (server side; design D13). That a revoked token is refused
// at /mcp is §6.1.
public class TokenEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync() =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null);

    /// <summary>A token for the browser's member, issued through a real device login.</summary>
    private static async Task<string> LoginAsync(PanopticonFactory factory, HttpClient browser, string machineName)
    {
        using var cli = factory.CreateClient();
        var start = await DeviceLoginTests.StartAsync(cli, machineName);
        (await browser.PostApiAsync($"{DeviceEndpoints.LoginPath(start.UserCode)}/approve", null, Ct)).Dispose();
        var poll = await DeviceLoginTests.PollAsync(cli, start.DeviceSecret);
        Assert.Equal(DevicePollStatus.Approved, poll.Status);
        return poll.Token!;
    }

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient http, Guid id) =>
        http.SendApiAsync(HttpMethod.Delete, $"{TokenEndpoints.TokensPath}/{id}", null, Ct);

    // "Token never shown again".
    [Fact]
    public async Task Listing_tokens_shows_machine_and_times_but_never_a_secret()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var secret = await LoginAsync(factory, browser, "vm-1");

        using var response = await browser.GetAsync(TokenEndpoints.TokensPath, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        var token = Assert.Single((await response.Content.ReadFromJsonAsync<List<TokenView>>(Ct))!);
        Assert.Equal("vm-1", token.MachineName);
        Assert.Null(token.LastUsedAt);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Secrets.ApiTokenPrefix, body, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }

    // "List and revoke" (server side).
    [Fact]
    public async Task Revoking_a_token_removes_it_from_the_list()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await LoginAsync(factory, browser, "vm-1");
        await LoginAsync(factory, browser, "vm-2");
        var vm1 = (await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!.Single(t => t.MachineName == "vm-1");

        using var revoked = await RevokeAsync(browser, vm1.Id);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var left = (await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!;
        Assert.Equal(["vm-2"], left.Select(t => t.MachineName));
        await using var db = factory.NewDb();
        Assert.NotNull((await db.ApiTokens.SingleAsync(t => t.Id == vm1.Id, Ct)).RevokedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await RevokeAsync(browser, vm1.Id)).StatusCode);
    }

    // "Someone else's token".
    [Fact]
    public async Task Someone_elses_token_is_not_found_not_listed_and_stays_valid()
    {
        await using var factory = await FactoryAsync();
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await browser.PostApiAsync(MemberEndpoints.MembersPath, new { email = "sam@example.com" }, Ct)).Dispose();
        using var samsBrowser = await factory.CreateSignedInClientAsync("sam@example.com", Ct);
        await LoginAsync(factory, samsBrowser, "sams-vm");
        Guid samsToken;
        await using (var db = factory.NewDb())
        {
            samsToken = await db.ApiTokens.Select(t => t.Id).SingleAsync(Ct);
        }

        Assert.Empty((await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!);
        using var refused = await RevokeAsync(browser, samsToken);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RevokeAsync(browser, Guid.NewGuid())).StatusCode);
        await using var after = factory.NewDb();
        Assert.Null((await after.ApiTokens.SingleAsync(Ct)).RevokedAt);
        Assert.Single((await samsBrowser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!);
    }

    [Fact]
    public async Task Token_endpoints_need_a_session_and_revoking_needs_the_request_header()
    {
        await using var factory = await FactoryAsync();
        using var anonymous = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(TokenEndpoints.TokensPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RevokeAsync(anonymous, Guid.NewGuid())).StatusCode);

        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await LoginAsync(factory, browser, "vm-1");
        var token = Assert.Single((await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!);
        using var noHeader = await browser.DeleteAsync($"{TokenEndpoints.TokensPath}/{token.Id}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Single((await browser.GetFromJsonAsync<List<TokenView>>(TokenEndpoints.TokensPath, Ct))!);
    }
}
