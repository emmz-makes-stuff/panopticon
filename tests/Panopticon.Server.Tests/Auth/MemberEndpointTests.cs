using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/team-members: "Member list", "Display names", "Removing a member" (design D11; projects D8), and inviting to the
// server (server-invites). Project invites are tested in ProjectEndpointTests.
public class MemberEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Sam = "sam@example.com";
    private const string Kit = "kit@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(string? initialMembers = null) =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null)
        {
            Settings = initialMembers is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { [InitialMembers.ConfigKey] = initialMembers },
        };

    private static async Task<List<MemberView>> ListAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<List<MemberView>>(MemberEndpoints.MembersPath, Ct))!;

    private static Task<HttpResponseMessage> RenameAsync(HttpClient http, string? displayName) =>
        http.SendApiAsync(HttpMethod.Patch, MemberEndpoints.MePath, new { displayName }, Ct);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient http, Guid id) =>
        http.SendApiAsync(HttpMethod.Delete, $"{MemberEndpoints.MembersPath}/{id}", null, Ct);

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString();

    /// <summary>Brings the address onto the server through a Project the browser's member owns, and returns their id.</summary>
    private static async Task<Guid> JoinAsync(PanopticonFactory factory, HttpClient browser, Guid project, string email, string role = "viewer")
    {
        await browser.InviteToProjectAsync(project, email, role, Ct);
        return await factory.MemberIdAsync(email, Ct);
    }

    // --- "Member list" ---

    [Fact]
    public async Task The_list_shows_people_who_share_a_Project_and_yourself_but_not_other_Projects_people()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        var acme = await emmz.CreateProjectAsync("ACME", Ct);
        await JoinAsync(factory, emmz, pan, Kit);
        await JoinAsync(factory, emmz, acme, Sam);
        using var kit = await factory.CreateSignedInClientAsync(Kit, Ct);

        Assert.Equal([PanopticonFactory.MemberEmail, Kit], (await ListAsync(kit)).Select(m => m.Email));
        Assert.Equal([PanopticonFactory.MemberEmail, Kit, Sam], (await ListAsync(emmz)).Select(m => m.Email));
    }

    [Fact]
    public async Task The_list_shows_email_name_and_join_date_but_not_removed_members()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await http.CreateProjectAsync("PAN", Ct);
        await JoinAsync(factory, http, pan, Sam);
        await JoinAsync(factory, http, pan, "alex@example.com");
        await using (var db = factory.NewDb())
        {
            await db.Members.Where(m => m.EmailKey == "alex@example.com")
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, DateTimeOffset.UtcNow), Ct);
        }

        var members = await ListAsync(http);

        Assert.Equal([PanopticonFactory.MemberEmail, Sam], members.Select(m => m.Email));
        Assert.All(members, m => Assert.NotEqual(default, m.JoinedAt));
        Assert.Equal(["member", "sam"], members.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task An_initial_member_sees_every_active_member_even_with_no_shared_Project()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await InviteAsync(emmz, Kit)).Dispose();

        var members = await ListAsync(emmz);

        Assert.Equal([PanopticonFactory.MemberEmail, Kit], members.Select(m => m.Email));
    }

    [Fact]
    public async Task People_invited_to_the_server_dont_see_each_other()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await InviteAsync(emmz, Kit)).Dispose();
        (await InviteAsync(emmz, Sam)).Dispose();
        using var kit = await factory.CreateSignedInClientAsync(Kit, Ct);

        Assert.Equal([Kit], (await ListAsync(kit)).Select(m => m.Email));
    }

    // --- "Inviting to the server", "Inviting an existing member", "Invitation email", "Invite rate limit" ---

    private static Task<HttpResponseMessage> InviteAsync(HttpClient http, string? email) =>
        http.PostApiAsync(MemberEndpoints.MembersPath, new { email }, Ct);

    /// <summary>
    /// Emails go out in the background, one at a time and in order. So once a later invite's email has arrived, an
    /// earlier refused invite would have sent its email already, if it had sent one.
    /// </summary>
    private static async Task AssertNoEmailToAsync(PanopticonFactory factory, HttpClient initialMember, string address)
    {
        const string Marker = "marker@example.com";
        (await InviteAsync(initialMember, Marker)).Dispose();
        await factory.Emails.WaitForAsync(Marker, cancellationToken: Ct);
        Assert.Empty(factory.Emails.To(address));
    }

    [Fact]
    public async Task An_initial_member_invites_a_friend_who_joins_no_Project_and_gets_an_email()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        using var renamed = await RenameAsync(emmz, "Emmz");

        using var invited = await InviteAsync(emmz, " kit@example.com ");

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var view = (await invited.Content.ReadFromJsonAsync<MemberView>(Ct))!;
        Assert.Equal(Kit, view.Email);
        Assert.Equal("kit", view.DisplayName);
        await using (var db = factory.NewDb())
        {
            var kit = await db.Members.SingleAsync(m => m.EmailKey == Kit, Ct);
            Assert.True(kit.IsActive);
            Assert.Equal(await factory.MemberIdAsync(PanopticonFactory.MemberEmail, Ct), kit.InvitedByMemberId);
            Assert.False(await db.ProjectMembers.AnyAsync(m => m.MemberId == kit.Id, Ct));
        }

        var email = Assert.Single(await factory.Emails.WaitForAsync(Kit, cancellationToken: Ct));
        Assert.Equal(ServerInviteEmail.Subject("Emmz"), email.Subject);
        Assert.Contains(PanopticonFactory.PublicUrl, email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sign-in/confirm", email.Text, StringComparison.Ordinal);

        // And they can sign in.
        using var kitsBrowser = await factory.CreateSignedInClientAsync(Kit, Ct);
        Assert.Equal(Kit, Assert.Single(await ListAsync(kitsBrowser)).Email);
    }

    [Fact]
    public async Task Inviting_a_removed_member_back_makes_them_active_with_no_Projects_or_tokens()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        var sam = await JoinAsync(factory, emmz, pan, Sam);
        await using (var db = factory.NewDb())
        {
            db.ApiTokens.Add(new ApiToken
            {
                MemberId = sam,
                SecretHash = Secrets.Hash("sams-token"),
                MachineName = "vm-1",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        (await RemoveAsync(emmz, sam)).Dispose();

        using var invited = await InviteAsync(emmz, Sam);

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        Assert.Equal(sam, (await invited.Content.ReadFromJsonAsync<MemberView>(Ct))!.Id);
        await using (var db = factory.NewDb())
        {
            Assert.True((await db.Members.SingleAsync(m => m.Id == sam, Ct)).IsActive);
            Assert.False(await db.ProjectMembers.AnyAsync(m => m.MemberId == sam, Ct));
            Assert.False(await db.ApiTokens.AnyAsync(t => t.MemberId == sam && t.RevokedAt == null, Ct));
        }

        await factory.Emails.WaitForAsync(Sam, cancellationToken: Ct);
    }

    [Fact]
    public async Task Only_initial_members_invite_to_the_server_and_nothing_changes_otherwise()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await JoinAsync(factory, emmz, await emmz.CreateProjectAsync("PAN", Ct), Sam, "owner");
        using var sam = await factory.CreateSignedInClientAsync(Sam, Ct);

        using var refused = await InviteAsync(sam, Kit);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Only an initial member", await DetailAsync(refused), StringComparison.Ordinal);
        Assert.False((await sam.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct)).GetProperty("canInviteMembers").GetBoolean());
        Assert.True((await emmz.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct)).GetProperty("canInviteMembers").GetBoolean());
        await using (var db = factory.NewDb())
        {
            Assert.False(await db.Members.AnyAsync(m => m.EmailKey == Kit, Ct));
        }

        await AssertNoEmailToAsync(factory, emmz, Kit);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_invalid_address_is_refused_and_nobody_is_added(string? email)
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var refused = await InviteAsync(emmz, email);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("valid email address", await DetailAsync(refused), StringComparison.Ordinal);
        await using var db = factory.NewDb();
        Assert.Equal(1, await db.Members.CountAsync(Ct));
    }

    [Fact]
    public async Task Inviting_an_active_member_is_refused_with_no_email()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        (await InviteAsync(emmz, Kit)).Dispose();
        await factory.Emails.WaitForAsync(Kit, cancellationToken: Ct);

        using var again = await InviteAsync(emmz, "Kit@Example.com");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal($"{Kit} is already a member.", await DetailAsync(again));
        const string Marker = "marker@example.com";
        (await InviteAsync(emmz, Marker)).Dispose();
        await factory.Emails.WaitForAsync(Marker, cancellationToken: Ct);
        Assert.Single(factory.Emails.To(Kit));
    }

    [Fact]
    public async Task Two_invites_of_one_new_address_at_once_add_one_member()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        var responses = await Task.WhenAll(InviteAsync(emmz, Kit), InviteAsync(emmz, Kit));

        try
        {
            Assert.Equal(
                [HttpStatusCode.Created, HttpStatusCode.Conflict],
                responses.Select(r => r.StatusCode).Order());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var db = factory.NewDb();
        Assert.Equal(1, await db.Members.CountAsync(m => m.EmailKey == Kit, Ct));
    }

    [Fact]
    public async Task Over_the_limit_an_invite_is_refused_adds_nobody_and_sends_nothing()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        using (var scope = factory.Services.CreateScope())
        {
            var limits = scope.ServiceProvider.GetRequiredService<AuthLimits>();
            var me = await factory.MemberIdAsync(PanopticonFactory.MemberEmail, Ct);
            while (limits.TryServerInvite(me))
            {
            }
        }

        using var refused = await InviteAsync(emmz, Kit);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Try again later", await DetailAsync(refused), StringComparison.Ordinal);
        await using var db = factory.NewDb();
        Assert.False(await db.Members.AnyAsync(m => m.EmailKey == Kit, Ct));
        Assert.Empty(factory.Emails.To(Kit));
    }

    // --- "Display names" ---

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

    // --- "Removing a member" ---

    [Fact]
    public async Task The_session_says_whether_you_can_remove_members()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await JoinAsync(factory, emmz, await emmz.CreateProjectAsync("PAN", Ct), Kit);
        using var kit = await factory.CreateSignedInClientAsync(Kit, Ct);

        Assert.True((await emmz.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct)).GetProperty("canRemoveMembers").GetBoolean());
        Assert.False((await kit.GetFromJsonAsync<JsonElement>(SessionEndpoints.SessionPath, Ct)).GetProperty("canRemoveMembers").GetBoolean());
    }

    // "Remove a colleague". Sam's reporters being refused at /mcp is the ApiToken scheme's; here the token is revoked,
    // which that scheme checks.
    [Fact]
    public async Task Remove_a_colleague_ends_their_sessions_revokes_their_tokens_takes_them_out_of_Projects_and_stops_sign_in()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await http.CreateProjectAsync("PAN", Ct);
        var sam = await JoinAsync(factory, http, pan, Sam, "editor");
        using var samsBrowser = await factory.CreateSignedInClientAsync(Sam, Ct);
        var before = factory.Emails.To(Sam).Count;
        (await samsBrowser.PostApiAsync(SignInEndpoints.SignInPath, new { email = Sam }, Ct)).Dispose();
        await factory.Emails.WaitForAsync(Sam, before + 1, Ct);
        var unusedLink = factory.Emails.LatestSignInSecret(Sam);
        await using (var db = factory.NewDb())
        {
            db.ApiTokens.Add(new ApiToken
            {
                MemberId = sam,
                SecretHash = Secrets.Hash("sams-token"),
                MachineName = "vm-1",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        using var removed = await RemoveAsync(http, sam);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain(await ListAsync(http), m => m.Id == sam);
        Assert.Equal(HttpStatusCode.Unauthorized, (await samsBrowser.GetAsync(SessionEndpoints.SessionPath, Ct)).StatusCode);
        await using (var db = factory.NewDb())
        {
            var row = await db.Members.SingleAsync(m => m.Id == sam, Ct);
            Assert.NotNull(row.RemovedAt);
            Assert.Equal(0, await db.Sessions.CountAsync(s => s.MemberId == sam, Ct));
            Assert.NotNull((await db.ApiTokens.SingleAsync(t => t.MemberId == sam, Ct)).RevokedAt);
            Assert.Equal(0, await db.ProjectMembers.CountAsync(m => m.MemberId == sam, Ct));
        }

        // A link sent before removal can't sign Sam in, even after Sam is invited back.
        await http.InviteToProjectAsync(pan, Sam, "viewer", Ct);
        using var stale = await factory.CreateBrowserClient().PostApiAsync(SignInEndpoints.ConfirmPath, new { secret = unusedLink }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
    }

    [Fact]
    public async Task Only_initial_members_remove_and_nothing_changes_otherwise()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        await JoinAsync(factory, emmz, pan, Kit, "owner");
        var sam = await JoinAsync(factory, emmz, pan, Sam);
        using var kit = await factory.CreateSignedInClientAsync(Kit, Ct);

        using var refused = await RemoveAsync(kit, sam);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Only an initial member", await DetailAsync(refused), StringComparison.Ordinal);
        Assert.Contains(await ListAsync(emmz), m => m.Id == sam);
    }

    [Fact]
    public async Task The_last_owner_of_a_Project_isnt_removed_until_it_has_another_owner()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = await JoinAsync(factory, emmz, await emmz.CreateProjectAsync("PAN", Ct), Sam);
        using var samsBrowser = await factory.CreateSignedInClientAsync(Sam, Ct);
        var acme = await samsBrowser.CreateProjectAsync("ACME", Ct);
        await samsBrowser.CreateProjectAsync("ZED", Ct);

        using var refused = await RemoveAsync(emmz, sam);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("only owner of ACME, ZED", await DetailAsync(refused), StringComparison.Ordinal);
        await using (var db = factory.NewDb())
        {
            Assert.Null((await db.Members.SingleAsync(m => m.Id == sam, Ct)).RemovedAt);
            Assert.Equal(3, await db.ProjectMembers.CountAsync(m => m.MemberId == sam, Ct));
        }

        await samsBrowser.InviteToProjectAsync(acme, PanopticonFactory.MemberEmail, "owner", Ct);
        using var stillZed = await RemoveAsync(emmz, sam);
        Assert.Contains("only owner of ZED", await DetailAsync(stillZed), StringComparison.Ordinal);
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

    [Fact]
    public async Task A_removed_members_sign_in_request_sends_no_email()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var sam = await JoinAsync(factory, http, await http.CreateProjectAsync("PAN", Ct), Sam);
        (await RemoveAsync(http, sam)).Dispose();

        using var requested = await factory.CreateBrowserClient().PostApiAsync(SignInEndpoints.SignInPath, new { email = Sam }, Ct);

        // Jobs run in the order they're queued: once a later member's email has arrived, Sam's would have too.
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var before = factory.Emails.To(PanopticonFactory.MemberEmail).Count;
        (await factory.CreateBrowserClient()
            .PostApiAsync(SignInEndpoints.SignInPath, new { email = PanopticonFactory.MemberEmail }, Ct)).Dispose();
        await factory.Emails.WaitForAsync(PanopticonFactory.MemberEmail, before + 1, Ct);
        Assert.Empty(factory.Emails.To(Sam));
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.SignInLinks.CountAsync(l => l.MemberId == sam, Ct));
    }

    // Two initial members removing each other at the same moment: exactly one removal wins, so somebody can still sign
    // in. Repeated on fresh servers, since one round may not overlap.
    [Fact]
    public async Task Two_members_removing_each_other_at_once_leave_one_active()
    {
        for (var round = 0; round < 5; round++)
        {
            await using var factory = await FactoryAsync($"{PanopticonFactory.MemberEmail},{Sam}");
            using var mine = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
            using var sams = await factory.CreateSignedInClientAsync(Sam, Ct);
            var me = await factory.MemberIdAsync(cancellationToken: Ct);
            var sam = await factory.MemberIdAsync(Sam, Ct);

            var results = await Task.WhenAll(RemoveAsync(mine, sam), RemoveAsync(sams, me));

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
        var sam = await JoinAsync(factory, http, await http.CreateProjectAsync("PAN", Ct), Sam);
        (await RemoveAsync(http, sam)).Dispose();

        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(http, sam)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(http, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Member_endpoints_need_a_session_and_writes_need_the_request_header()
    {
        await using var factory = await FactoryAsync();
        using var anonymous = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(MemberEndpoints.MembersPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenameAsync(anonymous, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RemoveAsync(anonymous, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await InviteAsync(anonymous, Kit)).StatusCode);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        using var noHeader = await http.PatchAsJsonAsync(MemberEndpoints.MePath, new { displayName = "x" }, Ct);
        using var inviteWithoutHeader = await http.PostAsJsonAsync(MemberEndpoints.MembersPath, new { email = Kit }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inviteWithoutHeader.StatusCode);
        Assert.Equal("member", Assert.Single(await ListAsync(http)).DisplayName);
    }
}
