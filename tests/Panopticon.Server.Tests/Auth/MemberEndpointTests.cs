using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/team-members: "Members invite members", "Member list", "Display names", "Removing a member" (design D11).
public class MemberEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync() =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null);

    private static async Task<List<MemberView>> ListAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<List<MemberView>>(MemberEndpoints.MembersPath, Ct))!;

    private static Task<HttpResponseMessage> InviteAsync(HttpClient http, string email, string? displayName = null) =>
        http.PostApiAsync(MemberEndpoints.MembersPath, new { email, displayName }, Ct);

    private static Task<HttpResponseMessage> RenameAsync(HttpClient http, string? displayName) =>
        http.SendApiAsync(HttpMethod.Patch, MemberEndpoints.MePath, new { displayName }, Ct);

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString();

    [Fact]
    public async Task Invite_a_colleague_who_is_then_listed_and_can_request_a_sign_in_link()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var invited = await InviteAsync(http, " Sam@Example.com ");

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var sam = Assert.Single(await ListAsync(http), m => m.Email == "Sam@Example.com");
        Assert.Equal("Sam", sam.DisplayName);

        await using (var db = factory.NewDb())
        {
            var row = await db.Members.SingleAsync(m => m.Id == sam.Id, Ct);
            var inviter = await db.Members.SingleAsync(m => m.EmailKey == PanopticonFactory.MemberEmail, Ct);
            Assert.Equal(inviter.Id, row.InvitedByMemberId);
        }

        using var samsBrowser = await factory.CreateSignedInClientAsync("sam@example.com", Ct);
        var session = await samsBrowser.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct);
        Assert.Equal(sam.Id, session.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Invite_with_a_display_name_uses_it_trimmed()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var invited = await InviteAsync(http, "sam@example.com", "  Sam Smith ");

        var sam = await invited.Content.ReadFromJsonAsync<MemberView>(Ct);
        Assert.Equal("Sam Smith", sam!.DisplayName);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("a@b@example.com")]
    public async Task Invalid_address_is_rejected_and_nobody_is_added(string email)
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var invited = await InviteAsync(http, email);

        Assert.Equal(HttpStatusCode.BadRequest, invited.StatusCode);
        Assert.Contains("valid email address", await DetailAsync(invited), StringComparison.Ordinal);
        await using var db = factory.NewDb();
        Assert.Equal(1, await db.Members.CountAsync(Ct));
    }

    [Fact]
    public async Task Invalid_display_name_is_rejected_and_nobody_is_added()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var invited = await InviteAsync(http, "sam@example.com", "   ");

        Assert.Equal(HttpStatusCode.BadRequest, invited.StatusCode);
        await using var db = factory.NewDb();
        Assert.Equal(1, await db.Members.CountAsync(Ct));
    }

    [Fact]
    public async Task Already_a_member_is_rejected_and_nothing_changes()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await InviteAsync(http, "sam@example.com", "Sam")).Dispose();

        using var again = await InviteAsync(http, "SAM@example.com", "Someone else");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already a member", await DetailAsync(again), StringComparison.Ordinal);
        var sam = Assert.Single(await ListAsync(http), m => m.Email == "sam@example.com");
        Assert.Equal("Sam", sam.DisplayName);
    }

    [Fact]
    public async Task Inviting_a_removed_member_makes_them_active_again_with_the_same_id()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = (await (await InviteAsync(http, "sam@example.com")).Content.ReadFromJsonAsync<MemberView>(Ct))!;
        await using (var db = factory.NewDb())
        {
            await db.Members.Where(m => m.Id == sam.Id)
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, DateTimeOffset.UtcNow), Ct);
        }

        Assert.DoesNotContain(await ListAsync(http), m => m.Id == sam.Id);

        using var again = await InviteAsync(http, "sam@example.com");

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var listed = Assert.Single(await ListAsync(http), m => m.Email == "sam@example.com");
        Assert.Equal(sam.Id, listed.Id);
        Assert.Equal("sam", listed.DisplayName);
    }

    [Fact]
    public async Task List_members_shows_active_members_with_email_name_and_join_date_but_not_removed_ones()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await InviteAsync(http, "sam@example.com")).Dispose();
        (await InviteAsync(http, "alex@example.com", "Alex")).Dispose();
        await using (var db = factory.NewDb())
        {
            await db.Members.Where(m => m.EmailKey == "alex@example.com")
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, DateTimeOffset.UtcNow), Ct);
        }

        var members = await ListAsync(http);

        Assert.Equal([PanopticonFactory.MemberEmail, "sam@example.com"], members.Select(m => m.Email));
        Assert.All(members, m => Assert.NotEqual(default, m.JoinedAt));
        Assert.Equal(["member", "sam"], members.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task Default_name_is_the_part_before_the_at()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var invited = await InviteAsync(http, "emmz@example.com");

        Assert.Equal("emmz", (await invited.Content.ReadFromJsonAsync<MemberView>(Ct))!.DisplayName);
    }

    [Fact]
    public async Task Rename_yourself_shows_in_the_list_and_the_session()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var renamed = await RenameAsync(http, "  Emmz ");

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Emmz", Assert.Single(await ListAsync(http)).DisplayName);
        var session = await http.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct);
        Assert.Equal("Emmz", session.GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Blank_name_is_rejected_and_the_name_stays(string? name)
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var renamed = await RenameAsync(http, name);

        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        Assert.Equal("member", Assert.Single(await ListAsync(http)).DisplayName);
    }

    [Fact]
    public async Task A_name_over_fifty_characters_is_rejected_but_fifty_is_fine()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var tooLong = await RenameAsync(http, new string('x', MemberNames.MaxLength + 1));
        using var justRight = await RenameAsync(http, new string('x', MemberNames.MaxLength));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, justRight.StatusCode);
    }

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient http, Guid id) =>
        http.SendApiAsync(HttpMethod.Delete, $"{MemberEndpoints.MembersPath}/{id}", null, Ct);

    // "Removing a member": "Remove a colleague". Sam's reporters being refused at /mcp is §6 (the ApiToken scheme);
    // here the token is revoked, which that scheme checks.
    [Fact]
    public async Task Remove_a_colleague_ends_their_sessions_revokes_their_tokens_and_stops_sign_in()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = (await (await InviteAsync(http, "sam@example.com")).Content.ReadFromJsonAsync<MemberView>(Ct))!;
        using var samsBrowser = await factory.CreateSignedInClientAsync("sam@example.com", Ct);
        var before = factory.Emails.To("sam@example.com").Count;
        (await samsBrowser.PostApiAsync(SignInEndpoints.SignInPath, new { email = "sam@example.com" }, Ct)).Dispose();
        await factory.Emails.WaitForAsync("sam@example.com", before + 1, Ct);
        var unusedLink = factory.Emails.LatestSignInSecret("sam@example.com");
        await using (var db = factory.NewDb())
        {
            db.ApiTokens.Add(new ApiToken
            {
                MemberId = sam.Id,
                SecretHash = Secrets.Hash("sams-token"),
                MachineName = "vm-1",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        using var removed = await RemoveAsync(http, sam.Id);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain(await ListAsync(http), m => m.Id == sam.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await samsBrowser.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
        await using (var db = factory.NewDb())
        {
            var row = await db.Members.SingleAsync(m => m.Id == sam.Id, Ct);
            Assert.NotNull(row.RemovedAt);
            Assert.Equal(0, await db.Sessions.CountAsync(s => s.MemberId == sam.Id, Ct));
            Assert.NotNull((await db.ApiTokens.SingleAsync(t => t.MemberId == sam.Id, Ct)).RevokedAt);
        }

        // A link sent before removal can't sign Sam in, even after Sam is invited back.
        (await InviteAsync(http, "sam@example.com")).Dispose();
        using var stale = await factory.CreateBrowserClient().PostApiAsync(SignInEndpoints.ConfirmPath, new { secret = unusedLink }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
    }

    [Fact]
    public async Task A_removed_members_sign_in_request_sends_no_email()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = (await (await InviteAsync(http, "sam@example.com")).Content.ReadFromJsonAsync<MemberView>(Ct))!;
        (await RemoveAsync(http, sam.Id)).Dispose();

        using var requested = await factory.CreateBrowserClient()
            .PostApiAsync(SignInEndpoints.SignInPath, new { email = "sam@example.com" }, Ct);

        // Jobs run in the order they're queued: once a later member's email has arrived, Sam's would have too.
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var before = factory.Emails.To(PanopticonFactory.MemberEmail).Count;
        (await factory.CreateBrowserClient()
            .PostApiAsync(SignInEndpoints.SignInPath, new { email = PanopticonFactory.MemberEmail }, Ct)).Dispose();
        await factory.Emails.WaitForAsync(PanopticonFactory.MemberEmail, before + 1, Ct);
        Assert.Empty(factory.Emails.To("sam@example.com"));
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.SignInLinks.CountAsync(l => l.MemberId == sam.Id, Ct));
    }

    [Fact]
    public async Task Cant_remove_yourself_and_nothing_changes()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var me = await http.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct);

        using var removed = await RemoveAsync(http, me.GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.BadRequest, removed.StatusCode);
        Assert.Contains("can't remove yourself", await DetailAsync(removed), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
        Assert.Single(await ListAsync(http));
    }

    // Two members removing each other at the same moment: exactly one removal wins, so somebody can still sign in.
    // Repeated on fresh servers, since one round may not overlap.
    [Fact]
    public async Task Two_members_removing_each_other_at_once_leave_one_active()
    {
        for (var round = 0; round < 5; round++)
        {
            await using var factory = await FactoryAsync();
            using var mine = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
            var me = (await mine.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct)).GetProperty("id").GetGuid();
            var sam = (await (await InviteAsync(mine, "sam@example.com")).Content.ReadFromJsonAsync<MemberView>(Ct))!;
            using var sams = await factory.CreateSignedInClientAsync("sam@example.com", Ct);

            var results = await Task.WhenAll(RemoveAsync(mine, sam.Id), RemoveAsync(sams, me));

            var statuses = results.Select(r => r.StatusCode).Order().ToList();
            Assert.Equal(HttpStatusCode.NoContent, statuses[0]);
            Assert.Contains(statuses[1], new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Conflict });
            await using var db = factory.NewDb();
            Assert.Equal(1, await db.Members.CountAsync(m => m.RemovedAt == null, Ct));
            foreach (var result in results)
            {
                result.Dispose();
            }
        }
    }

    [Fact]
    public async Task Removing_an_unknown_or_already_removed_member_is_a_404()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = (await (await InviteAsync(http, "sam@example.com")).Content.ReadFromJsonAsync<MemberView>(Ct))!;
        (await RemoveAsync(http, sam.Id)).Dispose();

        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(http, sam.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(http, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Member_endpoints_need_a_session_and_writes_need_the_request_header()
    {
        await using var factory = await FactoryAsync();
        using var anonymous = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(MemberEndpoints.MembersPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await InviteAsync(anonymous, "sam@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenameAsync(anonymous, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RemoveAsync(anonymous, Guid.NewGuid())).StatusCode);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        using var noHeader = await http.PostAsJsonAsync(MemberEndpoints.MembersPath, new { email = "sam@example.com" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Single(await ListAsync(http));
    }
}
