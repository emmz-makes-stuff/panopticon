using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Projects;

// spec projects: Creating a Project, Project codes, Project names, Membership roles, Inviting to a Project, Changing
// roles and removing, Projects list (design D5).
public class ProjectEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Sam = "sam@example.com";
    private const string Kit = "kit@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync() =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null);

    private static Task<HttpResponseMessage> CreateAsync(HttpClient http, string? name, string? code) =>
        http.PostApiAsync(ProjectEndpoints.ProjectsPath, new { name, code }, Ct);

    private static async Task<ProjectView> CreatedAsync(HttpClient http, string code = "PAN", string name = "Panopticon")
    {
        using var created = await CreateAsync(http, name, code);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<ProjectView>(Ct))!;
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient http, Guid projectId, string email, string role) =>
        http.PostApiAsync(ProjectEndpoints.MembersPath(projectId), new { email, role }, Ct);

    private static async Task<ProjectMemberView> InvitedAsync(HttpClient http, Guid projectId, string email, string role)
    {
        using var invited = await InviteAsync(http, projectId, email, role);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        return (await invited.Content.ReadFromJsonAsync<ProjectMemberView>(Ct))!;
    }

    private static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient http, Guid projectId, Guid memberId, string role) =>
        http.SendApiAsync(HttpMethod.Patch, $"{ProjectEndpoints.MembersPath(projectId)}/{memberId}", new { role }, Ct);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient http, Guid projectId, Guid memberId) =>
        http.SendApiAsync(HttpMethod.Delete, $"{ProjectEndpoints.MembersPath(projectId)}/{memberId}", null, Ct);

    private static async Task<List<ProjectView>> ListAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<List<ProjectView>>(ProjectEndpoints.ProjectsPath, Ct))!;

    private static async Task<ProjectDetails> DetailsAsync(HttpClient http, Guid projectId) =>
        (await http.GetFromJsonAsync<ProjectDetails>(ProjectEndpoints.ProjectPath(projectId), Ct))!;

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString();

    private static async Task<Guid> MemberIdAsync(PanopticonFactory factory, string email)
    {
        await using var db = factory.NewDb();
        return await db.Members.Where(m => m.EmailKey == email).Select(m => m.Id).SingleAsync(Ct);
    }

    [Fact]
    public async Task Create_a_Project_with_a_new_id_its_code_in_capitals_and_the_creator_as_owner()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        var project = await CreatedAsync(http, code: " pan ", name: " Panopticon ");

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(("Panopticon", "PAN", ProjectRoles.Owner), (project.Name, project.Code, project.Role));
        Assert.Equal([project], await ListAsync(http));
        var details = await DetailsAsync(http, project.Id);
        var owner = Assert.Single(details.Members);
        Assert.Equal((PanopticonFactory.MemberEmail, ProjectRoles.Owner), (owner.Email, owner.Role));
    }

    [Fact]
    public async Task A_code_taken_in_any_case_is_rejected_and_nothing_is_created()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await CreatedAsync(http, "PAN");

        using var again = await CreateAsync(http, "Another", "pan");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("PAN is taken", await DetailAsync(again), StringComparison.Ordinal);
        await using var db = factory.NewDb();
        Assert.Equal(1, await db.Projects.CountAsync(Ct));
    }

    [Theory]
    [InlineData("P-1")]
    [InlineData("P")]
    [InlineData("1AB")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_invalid_code_is_rejected_saying_what_a_code_may_contain(string? code)
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var created = await CreateAsync(http, "Panopticon", code);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(ProjectCodes.Rule, await DetailAsync(created));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_name_is_rejected(string? name)
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);

        using var created = await CreateAsync(http, name, "PAN");

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("needs a name", await DetailAsync(created), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_owner_renames_the_Project_and_its_id_and_code_stay()
    {
        await using var factory = await FactoryAsync();
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var project = await CreatedAsync(http);

        using var renamed = await http.SendApiAsync(
            HttpMethod.Patch,
            ProjectEndpoints.ProjectPath(project.Id),
            new { name = "Panopticon server", code = "XYZ" },
            Ct);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var details = await DetailsAsync(http, project.Id);
        Assert.Equal((project.Id, "Panopticon server", "PAN"), (details.Id, details.Name, details.Code));
    }

    [Fact]
    public async Task Only_your_own_Projects_are_listed_and_another_answers_not_found()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz, "PAN");
        var acme = await CreatedAsync(emmz, "ACME");
        await InvitedAsync(emmz, pan.Id, Sam, ProjectRoles.Viewer);
        using var sam = await factory.CreateSignedInClientAsync(Sam, Ct);

        Assert.Equal(["PAN"], (await ListAsync(sam)).Select(p => p.Code));
        using var other = await sam.GetAsync(ProjectEndpoints.ProjectPath(acme.Id), Ct);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var rename = await sam.SendApiAsync(HttpMethod.Patch, ProjectEndpoints.ProjectPath(acme.Id), new { name = "Mine" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
    }

    [Fact]
    public async Task Inviting_someone_new_lets_them_sign_in_as_a_member_with_that_role()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);

        var invited = await InvitedAsync(emmz, pan.Id, " Sam@Example.com ", ProjectRoles.Viewer);

        Assert.Equal(("Sam@Example.com", "Sam", ProjectRoles.Viewer), (invited.Email, invited.DisplayName, invited.Role));
        using var sam = await factory.CreateSignedInClientAsync(Sam, Ct);
        var mine = Assert.Single(await ListAsync(sam));
        Assert.Equal((pan.Id, ProjectRoles.Viewer), (mine.Id, mine.Role));
    }

    [Fact]
    public async Task Inviting_someone_already_on_the_server_or_removed_from_it_adds_them_with_that_role()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz, "PAN");
        var acme = await CreatedAsync(emmz, "ACME");
        var kit = await InvitedAsync(emmz, pan.Id, Kit, ProjectRoles.Viewer);
        await InvitedAsync(emmz, acme.Id, Sam, ProjectRoles.Viewer);
        await using (var db = factory.NewDb())
        {
            await db.Members.Where(m => m.EmailKey == Sam).ExecuteUpdateAsync(m => m.SetProperty(x => x.RemovedAt, DateTimeOffset.UnixEpoch), Ct);
        }

        var kitAgain = await InvitedAsync(emmz, acme.Id, Kit, ProjectRoles.Editor);
        var samAgain = await InvitedAsync(emmz, pan.Id, Sam, ProjectRoles.Owner);

        Assert.Equal((kit.MemberId, ProjectRoles.Editor), (kitAgain.MemberId, kitAgain.Role));
        Assert.Equal(ProjectRoles.Owner, samAgain.Role);
        await using var after = factory.NewDb();
        Assert.Null((await after.Members.SingleAsync(m => m.EmailKey == Sam, Ct)).RemovedAt);
        Assert.Equal(1, await after.Members.CountAsync(m => m.EmailKey == Sam, Ct));
    }

    [Fact]
    public async Task Inviting_someone_already_in_the_Project_is_rejected()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);
        await InvitedAsync(emmz, pan.Id, Sam, ProjectRoles.Viewer);

        using var again = await InviteAsync(emmz, pan.Id, "SAM@example.com", ProjectRoles.Owner);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already in this Project", await DetailAsync(again), StringComparison.Ordinal);
        Assert.Equal(ProjectRoles.Viewer, (await DetailsAsync(emmz, pan.Id)).Members.Single(m => m.Email == Sam).Role);
    }

    [Theory]
    [InlineData("not-an-email", ProjectRoles.Viewer, "valid email address")]
    [InlineData(Sam, "admin", "owner, editor or viewer")]
    [InlineData(Sam, null, "owner, editor or viewer")]
    public async Task An_invalid_invite_is_rejected_and_nobody_is_added(string email, string? role, string message)
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);

        using var invited = await InviteAsync(emmz, pan.Id, email, role!);

        Assert.Equal(HttpStatusCode.BadRequest, invited.StatusCode);
        Assert.Contains(message, await DetailAsync(invited), StringComparison.Ordinal);
        Assert.Single((await DetailsAsync(emmz, pan.Id)).Members);
    }

    [Theory]
    [InlineData(ProjectRoles.Editor)]
    [InlineData(ProjectRoles.Viewer)]
    public async Task Only_owners_invite_rename_change_roles_and_remove(string role)
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);
        await InvitedAsync(emmz, pan.Id, Kit, role);
        using var kit = await factory.CreateSignedInClientAsync(Kit, Ct);
        var emmzId = await MemberIdAsync(factory, PanopticonFactory.MemberEmail);

        using var invite = await InviteAsync(kit, pan.Id, Sam, ProjectRoles.Viewer);
        using var rename = await kit.SendApiAsync(HttpMethod.Patch, ProjectEndpoints.ProjectPath(pan.Id), new { name = "Mine" }, Ct);
        using var demote = await ChangeRoleAsync(kit, pan.Id, emmzId, ProjectRoles.Viewer);
        using var remove = await RemoveAsync(kit, pan.Id, emmzId);

        Assert.All([invite, rename, demote, remove], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        var details = await DetailsAsync(emmz, pan.Id);
        Assert.Equal(("Panopticon", 2), (details.Name, details.Members.Count));
        Assert.Equal(ProjectRoles.Owner, details.Members.Single(m => m.MemberId == emmzId).Role);
    }

    [Fact]
    public async Task An_owner_makes_a_second_owner_and_can_then_step_down()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);
        var kit = await InvitedAsync(emmz, pan.Id, Kit, ProjectRoles.Viewer);
        var emmzId = await MemberIdAsync(factory, PanopticonFactory.MemberEmail);

        using var promoted = await ChangeRoleAsync(emmz, pan.Id, kit.MemberId, ProjectRoles.Owner);
        using var stepped = await ChangeRoleAsync(emmz, pan.Id, emmzId, ProjectRoles.Editor);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (promoted.StatusCode, stepped.StatusCode));
        var roles = (await DetailsAsync(emmz, pan.Id)).Members.ToDictionary(m => m.MemberId, m => m.Role);
        Assert.Equal((ProjectRoles.Owner, ProjectRoles.Editor), (roles[kit.MemberId], roles[emmzId]));
    }

    [Fact]
    public async Task The_last_owner_can_neither_step_down_nor_leave()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);
        var emmzId = await MemberIdAsync(factory, PanopticonFactory.MemberEmail);

        using var stepped = await ChangeRoleAsync(emmz, pan.Id, emmzId, ProjectRoles.Editor);
        using var left = await RemoveAsync(emmz, pan.Id, emmzId);

        Assert.All([stepped, left], r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Contains("needs an owner", await DetailAsync(left), StringComparison.Ordinal);
        Assert.Equal(ProjectRoles.Owner, Assert.Single((await DetailsAsync(emmz, pan.Id)).Members).Role);
    }

    [Fact]
    public async Task Someone_removed_from_a_Project_no_longer_sees_it_and_a_member_can_leave()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);
        var sam = await InvitedAsync(emmz, pan.Id, Sam, ProjectRoles.Viewer);
        var kit = await InvitedAsync(emmz, pan.Id, Kit, ProjectRoles.Editor);
        using var samsBrowser = await factory.CreateSignedInClientAsync(Sam, Ct);
        using var kitsBrowser = await factory.CreateSignedInClientAsync(Kit, Ct);

        using var removed = await RemoveAsync(emmz, pan.Id, sam.MemberId);
        using var left = await RemoveAsync(kitsBrowser, pan.Id, kit.MemberId);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (removed.StatusCode, left.StatusCode));
        Assert.Empty(await ListAsync(samsBrowser));
        Assert.Empty(await ListAsync(kitsBrowser));
        using var page = await samsBrowser.GetAsync(ProjectEndpoints.ProjectPath(pan.Id), Ct);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Single((await DetailsAsync(emmz, pan.Id)).Members);
    }

    [Fact]
    public async Task Two_owners_demoting_each_other_at_once_leave_one_owner()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await CreatedAsync(emmz);
        var kit = await InvitedAsync(emmz, pan.Id, Kit, ProjectRoles.Owner);
        using var kitsBrowser = await factory.CreateSignedInClientAsync(Kit, Ct);
        var emmzId = await MemberIdAsync(factory, PanopticonFactory.MemberEmail);

        var results = await Task.WhenAll(
            ChangeRoleAsync(emmz, pan.Id, kit.MemberId, ProjectRoles.Viewer),
            ChangeRoleAsync(kitsBrowser, pan.Id, emmzId, ProjectRoles.Viewer));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Forbidden],
            results.Select(r => r.StatusCode).Order());
        foreach (var result in results)
        {
            result.Dispose();
        }

        await using var db = factory.NewDb();
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == pan.Id && m.Role == ProjectRoles.Owner, Ct));
    }
}
