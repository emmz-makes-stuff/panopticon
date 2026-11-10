using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Live;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// spec tags: "Tagging a change on the dashboard" (design D4).
public class ChangeTagEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> TagAsync(HttpClient http, Guid change, Guid[]? add = null, Guid[]? remove = null) =>
        http.PostApiAsync(ChangeTagEndpoints.TagsPath(change), new { add, remove }, Ct);

    private static async Task<List<string>> TagsOnAsync(HttpClient http, Guid change)
    {
        var page = await http.GetFromJsonAsync<JsonElement>($"{DashboardEndpoints.ChangesPath}/{change}", Ct);
        return [.. page.GetProperty("card").GetProperty("projectTags").EnumerateArray().Select(t => t.GetProperty("key").GetString()!)];
    }

    [Fact]
    public async Task An_editor_tags_a_change_and_gets_its_card_back()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        using var tagged = await TagAsync(world.Sam, change, add: [bug]);

        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        var card = await tagged.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var tag = Assert.Single(card.GetProperty("projectTags").EnumerateArray());
        Assert.Equal((bug.ToString(), "bug"), (tag.GetProperty("id").GetString(), tag.GetProperty("key").GetString()));
        Assert.Equal(["bug"], await TagsOnAsync(world.Kit, change));
    }

    [Fact]
    public async Task Adding_and_removing_at_once_and_repeats_are_no_ops()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var onboarding = await world.AddPanTagAsync("area: onboarding", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);
        using (var first = await TagAsync(world.Sam, change, add: [bug]))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        // A tag in both lists ends up on the change; adding one it has is a no-op.
        using var both = await TagAsync(world.Sam, change, add: [onboarding, bug], remove: [bug]);
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        Assert.Equal(["area", "bug"], await TagsOnAsync(world.Sam, change));

        using var swapped = await TagAsync(world.Sam, change, add: [onboarding], remove: [bug]);
        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);
        Assert.Equal(["area"], await TagsOnAsync(world.Sam, change));

        // Removing one it lacks is a no-op too.
        using var again = await TagAsync(world.Sam, change, remove: [bug]);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(["area"], await TagsOnAsync(world.Sam, change));
    }

    [Fact]
    public async Task An_archived_change_can_be_tagged_and_isnt_sent_live()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        var archived = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan, archived: true), Ct);
        using var subscription = world.Factory.Services.GetRequiredService<ChangeBroadcaster>().Subscribe();

        using var tagged = await TagAsync(world.Emmz, archived, add: [bug]);

        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        Assert.Equal(["bug"], await TagsOnAsync(world.Emmz, archived));
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Tagging_an_active_change_sends_its_card_live()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);
        using var subscription = world.Factory.Services.GetRequiredService<ChangeBroadcaster>().Subscribe();

        using var tagged = await TagAsync(world.Sam, change, add: [bug]);

        var updated = Assert.IsType<ChangeEvent.Updated>(await subscription.Reader.ReadAsync(Ct));
        Assert.Equal(change, updated.Card.ChangeId);
        Assert.Equal([bug], updated.Card.ProjectTags.Select(t => t.Id));
        Assert.Equal(new ChangeScope(world.Pan, null), updated.Scope);
    }

    [Fact]
    public async Task A_viewer_cannot_tag()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        using var tagged = await TagAsync(world.Kit, change, add: [bug]);

        Assert.Equal(HttpStatusCode.Forbidden, tagged.StatusCode);
        Assert.Empty(await TagsOnAsync(world.Kit, change));
    }

    [Fact]
    public async Task A_change_you_cannot_see_is_not_found()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var acme = await world.Emmz.CreateProjectAsync("ACME", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("acme-work", acme), Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);

        using var tagged = await TagAsync(world.Sam, change, add: [bug]);

        Assert.Equal(HttpStatusCode.NotFound, tagged.StatusCode);
    }

    [Fact]
    public async Task Another_Projects_tag_is_refused()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var acme = await world.Emmz.CreateProjectAsync("ACME", Ct);
        using var acmeTag = await world.Emmz.PostApiAsync(ProjectTagEndpoints.TagsPath(acme), new { tag = "bug" }, Ct);
        var theirs = (await acmeTag.Content.ReadFromJsonAsync<ProjectTagView>(Ct))!.Id;
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        using var tagged = await TagAsync(world.Emmz, change, add: [theirs]);

        Assert.Equal(HttpStatusCode.BadRequest, tagged.StatusCode);
        Assert.Empty(await TagsOnAsync(world.Emmz, change));
    }

    [Fact]
    public async Task A_personal_change_has_no_project_tags()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("scratch", project: null), Ct);

        using var tagged = await TagAsync(world.Emmz, change, add: [bug]);

        Assert.Equal(HttpStatusCode.BadRequest, tagged.StatusCode);
    }

    [Fact]
    public async Task An_empty_request_is_refused()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        using var tagged = await TagAsync(world.Emmz, change);

        Assert.Equal(HttpStatusCode.BadRequest, tagged.StatusCode);
    }
}
