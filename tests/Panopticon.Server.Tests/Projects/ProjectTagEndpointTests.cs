using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Server.Data;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Projects;

// spec tags: A Project's tags, Managing a Project's tags, Renaming a tag, Deleting a tag (server side); spec projects:
// Membership roles, Project page (design D3).
public class ProjectTagEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Sam = "sam@example.com";
    private const string Kit = "kit@example.com";
    private const string Ola = "ola@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A Project PAN owned by the factory's member, with Sam as an editor and Kit as a viewer.</summary>
    private async Task<(PanopticonFactory Factory, HttpClient Owner, HttpClient Editor, HttpClient Viewer, Guid Project)> SetUpAsync()
    {
        var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null);
        var owner = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var project = await owner.CreateProjectAsync("PAN", Ct);
        await owner.InviteToProjectAsync(project, Sam, ProjectRoles.Editor, Ct);
        await owner.InviteToProjectAsync(project, Kit, ProjectRoles.Viewer, Ct);
        return (factory, owner, await factory.CreateSignedInClientAsync(Sam, Ct), await factory.CreateSignedInClientAsync(Kit, Ct), project);
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient http, Guid project, string? tag) =>
        http.PostApiAsync(ProjectTagEndpoints.TagsPath(project), new { tag }, Ct);

    private static async Task<ProjectTagView> AddedAsync(HttpClient http, Guid project, string tag)
    {
        using var added = await AddAsync(http, project, tag);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        return (await added.Content.ReadFromJsonAsync<ProjectTagView>(Ct))!;
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient http, Guid project, Guid tagId, string tag) =>
        http.SendApiAsync(HttpMethod.Patch, ProjectTagEndpoints.TagPath(project, tagId), new { tag }, Ct);

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient http, Guid project, Guid tagId) =>
        http.SendApiAsync(HttpMethod.Delete, ProjectTagEndpoints.TagPath(project, tagId), null, Ct);

    private static async Task<ProjectDetails> DetailsAsync(HttpClient http, Guid project) =>
        (await http.GetFromJsonAsync<ProjectDetails>(ProjectEndpoints.ProjectPath(project), Ct))!;

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString();

    /// <summary>Puts each tag on a new change of the Project, straight into the database (tagging changes is §3).</summary>
    private static async Task TagChangesAsync(PanopticonFactory factory, Guid project, Guid tagId, int changes)
    {
        await using var db = factory.NewDb();
        var repo = new Repo { Host = "github.com", Owner = "emmz", Name = "panopticon", HostKey = "github.com", OwnerKey = "emmz", NameKey = "panopticon" };
        db.Repos.Add(repo);
        for (var i = 0; i < changes; i++)
        {
            var change = new Change
            {
                Id = Guid.NewGuid(),
                RepoId = repo.Id,
                Name = $"change-{i}",
                NameKey = $"change-{i}",
                Snapshot = new ProgressSnapshot(),
                ProjectId = project,
                ArchivedAt = i == 0 ? DateTimeOffset.UnixEpoch : null,
                ArchiveFolder = i == 0 ? "2026-11-10-change-0" : null,
            };
            db.Changes.Add(change);
            db.ChangeProjectTags.Add(new ChangeProjectTag { ChangeId = change.Id, TagId = tagId });
        }

        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task An_editor_adds_a_tag_and_every_member_sees_it_with_the_code()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;

        var tag = await AddedAsync(editor, project, "  Area:  Onboarding ");

        Assert.Equal(("Area", "Onboarding", "Area: Onboarding", 0), (tag.Key, tag.Value, tag.Text, tag.ChangeCount));
        var seen = Assert.Single((await DetailsAsync(viewer, project)).Tags);
        Assert.Equal(tag, seen);
    }

    [Fact]
    public async Task An_owner_manages_tags_too()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;

        var tag = await AddedAsync(owner, project, "bug");
        using var renamed = await RenameAsync(owner, project, tag.Id, "defect");
        using var deleted = await DeleteAsync(owner, project, tag.Id);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task An_editor_manages_tags_but_cannot_invite()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;

        await AddedAsync(editor, project, "bug");
        using var invite = await editor.PostApiAsync(ProjectEndpoints.MembersPath(project), new { email = Ola, role = ProjectRoles.Viewer }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
    }

    [Fact]
    public async Task A_viewer_can_see_the_tags_and_change_nothing()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var tag = await AddedAsync(owner, project, "bug");

        using var add = await AddAsync(viewer, project, "defect");
        using var rename = await RenameAsync(viewer, project, tag.Id, "defect");
        using var delete = await DeleteAsync(viewer, project, tag.Id);

        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], [add.StatusCode, rename.StatusCode, delete.StatusCode]);
        Assert.Equal("Only the Project's owners and editors can change its tags.", await DetailAsync(add));
        Assert.Equal(["bug"], (await DetailsAsync(viewer, project)).Tags.Select(t => t.Text));
    }

    [Fact]
    public async Task Someone_outside_the_Project_gets_not_found()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var tag = await AddedAsync(owner, project, "bug");
        await owner.InviteToProjectAsync(await owner.CreateProjectAsync("ACME", Ct), Ola, ProjectRoles.Owner, Ct);
        using var outsider = await factory.CreateSignedInClientAsync(Ola, Ct);

        using var add = await AddAsync(outsider, project, "defect");
        using var rename = await RenameAsync(outsider, project, tag.Id, "defect");
        using var delete = await DeleteAsync(outsider, project, tag.Id);

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], [add.StatusCode, rename.StatusCode, delete.StatusCode]);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("key:")]
    [InlineData(": value")]
    [InlineData(null)]
    public async Task A_malformed_tag_is_refused_with_its_reason(string? text)
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;

        using var added = await AddAsync(editor, project, text);

        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        Assert.False(string.IsNullOrEmpty(await DetailAsync(added)));
        Assert.Empty((await DetailsAsync(editor, project)).Tags);
    }

    [Fact]
    public async Task A_duplicate_is_refused_naming_the_existing_tag()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        await AddedAsync(editor, project, "area: onboarding");
        var billing = await AddedAsync(editor, project, "area: billing");

        using var added = await AddAsync(editor, project, "AREA: Onboarding");
        using var renamed = await RenameAsync(editor, project, billing.Id, "Area: Onboarding");

        Assert.Equal(HttpStatusCode.Conflict, added.StatusCode);
        Assert.Equal("PAN/area: onboarding is already one of this Project's tags.", await DetailAsync(added));
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
        Assert.Equal("PAN/area: onboarding is already one of this Project's tags.", await DetailAsync(renamed));
    }

    [Fact]
    public async Task Renaming_a_tag_to_its_own_name_in_other_casing_is_fine()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var tag = await AddedAsync(editor, project, "bug");

        using var renamed = await RenameAsync(editor, project, tag.Id, "Bug");

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(["Bug"], (await DetailsAsync(editor, project)).Tags.Select(t => t.Text));
    }

    [Fact]
    public async Task A_rename_changes_the_tag_on_every_change_carrying_it()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var tag = await AddedAsync(editor, project, "area: onboarding");
        await TagChangesAsync(factory, project, tag.Id, changes: 3);

        using var renamed = await RenameAsync(editor, project, tag.Id, "area: first-run");

        var view = (await renamed.Content.ReadFromJsonAsync<ProjectTagView>(Ct))!;
        Assert.Equal((tag.Id, "area: first-run", 3), (view.Id, view.Text, view.ChangeCount));
        await using var db = factory.NewDb();
        var onChanges = await db.ChangeProjectTags.Select(c => c.Tag.Value).ToListAsync(Ct);
        Assert.Equal(["first-run", "first-run", "first-run"], onChanges);
    }

    [Fact]
    public async Task Deleting_a_tag_takes_it_off_every_change_archived_included()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var bug = await AddedAsync(editor, project, "bug");
        var onboarding = await AddedAsync(editor, project, "area: onboarding");
        await TagChangesAsync(factory, project, bug.Id, changes: 12);
        Assert.Equal(12, (await DetailsAsync(editor, project)).Tags.Single(t => t.Id == bug.Id).ChangeCount);

        using var deleted = await DeleteAsync(editor, project, bug.Id);
        using var again = await DeleteAsync(editor, project, bug.Id);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal([onboarding.Id], (await DetailsAsync(editor, project)).Tags.Select(t => t.Id));
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.ChangeProjectTags.CountAsync(Ct));
    }

    // tags D6: every write tells the Project's open pages its tags as they are now.
    [Fact]
    public async Task Each_tag_write_publishes_the_Projects_tags_as_they_are_now()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var bug = await AddedAsync(editor, project, "bug");
        using var subscription = factory.Services.GetRequiredService<ChangeBroadcaster>().Subscribe();

        using var renamed = await RenameAsync(editor, project, bug.Id, "defect");
        using var added = await AddAsync(editor, project, "area: onboarding");
        using var deleted = await DeleteAsync(editor, project, bug.Id);

        var published = new List<ChangeEvent.TagsChanged>();
        while (published.Count < 3)
        {
            published.Add(Assert.IsType<ChangeEvent.TagsChanged>(await subscription.Reader.ReadAsync(Ct)));
        }

        Assert.All(published, p => Assert.Equal(project, p.ProjectId));
        Assert.Equal([new ProjectTagRef(bug.Id, "defect", null)], published[0].Tags);
        Assert.Equal(["area: onboarding", "defect"], published[1].Tags.Select(t => t.Value is null ? t.Key : $"{t.Key}: {t.Value}"));
        Assert.Equal(["area"], published[2].Tags.Select(t => t.Key));
    }

    [Fact]
    public async Task Tags_are_listed_by_key_then_value_with_a_bare_key_first()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        foreach (var text in new[] { "bug", "area: onboarding", "Area: Billing", "area" })
        {
            await AddedAsync(editor, project, text);
        }

        Assert.Equal(["area", "Area: Billing", "area: onboarding", "bug"], (await DetailsAsync(viewer, project)).Tags.Select(t => t.Text));
    }

    // Review fix 11.4: ordinal, as the cards sort, not the database's collation (which ignores '-', '.' and '_').
    [Fact]
    public async Task Tags_are_in_ordinal_order_in_the_list_and_the_event_as_on_the_cards()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        string[] ordinal = ["a-b", "a.b", "a1", "a_b", "ab"];
        foreach (var text in new[] { "ab", "a_b", "a1", "a.b" })
        {
            await AddedAsync(editor, project, text);
        }

        using var subscription = factory.Services.GetRequiredService<ChangeBroadcaster>().Subscribe();
        await AddedAsync(editor, project, "a-b");
        var published = Assert.IsType<ChangeEvent.TagsChanged>(await subscription.Reader.ReadAsync(Ct));

        Assert.Equal(ordinal, (await DetailsAsync(viewer, project)).Tags.Select(t => t.Text));
        Assert.Equal(ordinal, published.Tags.Select(t => t.Key));
    }

    [Fact]
    public async Task Another_Projects_tag_cannot_be_renamed_or_deleted_through_this_one()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        var acme = await owner.CreateProjectAsync("ACME", Ct);
        var theirs = await AddedAsync(owner, acme, "bug");

        using var renamed = await RenameAsync(owner, project, theirs.Id, "defect");
        using var deleted = await DeleteAsync(owner, project, theirs.Id);

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound], [renamed.StatusCode, deleted.StatusCode]);
        Assert.Equal(["bug"], (await DetailsAsync(owner, acme)).Tags.Select(t => t.Text));
    }

    [Fact]
    public async Task A_Project_holds_at_most_200_tags_even_when_two_editors_add_at_once()
    {
        var (factory, owner, editor, viewer, project) = await SetUpAsync();
        await using var _ = factory;
        using var __ = owner;
        using var ___ = editor;
        using var ____ = viewer;
        await using (var db = factory.NewDb())
        {
            db.ProjectTags.AddRange(Enumerable.Range(1, 199).Select(i => new ProjectTag
            {
                Id = Guid.NewGuid(),
                ProjectId = project,
                Key = $"t{i}",
                KeyKey = $"t{i}",
                ValueKey = string.Empty,
            }));
            await db.SaveChangesAsync(Ct);
        }

        var results = await Task.WhenAll(AddAsync(owner, project, "one"), AddAsync(editor, project, "two"));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
        var refused = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("This Project has 200 tags, the most it can have. Delete one first.", await DetailAsync(refused));
        Assert.Equal(200, (await DetailsAsync(owner, project)).Tags.Count);
        foreach (var result in results)
        {
            result.Dispose();
        }
    }
}
