using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// spec tags: "Tags shown on changes" and "Personal tags are private" (design D6), on the card, the change page and the
// history.
public class TagReadTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Text(JsonElement tag) =>
        tag.GetProperty("value").GetString() is { } value ? $"{tag.GetProperty("key").GetString()}: {value}" : tag.GetProperty("key").GetString()!;

    // "project tags | personal tags", each comma-separated in the order given.
    private static string TagsOf(JsonElement withTags) =>
        $"{string.Join(", ", withTags.GetProperty("projectTags").EnumerateArray().Select(Text))} | "
        + string.Join(", ", withTags.GetProperty("personalTags").EnumerateArray().Select(Text));

    private static async Task<JsonElement> CardAsync(HttpClient http, Guid change) =>
        (await http.GetFromJsonAsync<JsonElement>(DashboardEndpoints.ChangesPath, Ct))
            .EnumerateArray().Single(c => c.GetProperty("changeId").GetGuid() == change);

    private static async Task<JsonElement> PageCardAsync(HttpClient http, Guid change) =>
        (await http.GetFromJsonAsync<JsonElement>($"{DashboardEndpoints.ChangesPath}/{change}", Ct)).GetProperty("card");

    private static async Task<JsonElement> HistoryEntryAsync(HttpClient http, Guid change) =>
        (await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, Ct)).GetProperty("changes")
            .EnumerateArray().Single(c => c.GetProperty("changeId").GetGuid() == change);

    private static async Task TagAsync(HttpClient http, Guid change, Guid tag)
    {
        using var tagged = await http.PostApiAsync(ChangeTagEndpoints.TagsPath(change), new { add = new[] { tag } }, Ct);
        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
    }

    [Fact]
    public async Task A_card_shows_the_project_tags_and_the_viewers_own_personal_tags()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["dev", "area: cli"]), Ct);
        await TagAsync(world.Sam, change, bug);

        Assert.Equal("bug | area: cli, dev", TagsOf(await CardAsync(world.Emmz, change)));
        Assert.Equal("bug | area: cli, dev", TagsOf(await PageCardAsync(world.Emmz, change)));
    }

    [Fact]
    public async Task A_change_with_no_tags_has_none_on_its_card()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        Assert.Equal(" | ", TagsOf(await CardAsync(world.Emmz, change)));
    }

    [Fact]
    public async Task Each_member_sees_only_their_own_personal_tags_everywhere()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["dev"]), Ct);
        await using var sams = await world.Factory.CreateMcpClientAsync(await world.Factory.CreateApiTokenAsync(TagWorld.SamEmail, cancellationToken: Ct), Ct);
        await TagWorld.ReportAsync(sams, TagWorld.Report("walking-skeleton", world.Pan, "sams-laptop", personalTags: ["secret"]), Ct);
        await TagAsync(world.Sam, change, bug);

        foreach (var read in new Func<HttpClient, Guid, Task<JsonElement>>[] { CardAsync, PageCardAsync })
        {
            Assert.Equal("bug | dev", TagsOf(await read(world.Emmz, change)));
            Assert.Equal("bug | secret", TagsOf(await read(world.Sam, change)));
            Assert.Equal("bug | ", TagsOf(await read(world.Kit, change)));
        }

        // Archived, they stay each member's own in the history and on the page.
        await TagWorld.ReportAsync(world.Reporter, TagWorld.Report("walking-skeleton", world.Pan, archived: true), Ct);
        Assert.Equal("bug | dev", TagsOf(await HistoryEntryAsync(world.Emmz, change)));
        Assert.Equal("bug | secret", TagsOf(await HistoryEntryAsync(world.Sam, change)));
        Assert.Equal("bug | ", TagsOf(await HistoryEntryAsync(world.Kit, change)));
        Assert.Equal("bug | dev", TagsOf(await PageCardAsync(world.Emmz, change)));
        Assert.Equal("bug | ", TagsOf(await PageCardAsync(world.Kit, change)));
    }

    [Fact]
    public async Task A_members_agents_tags_combine_once_each_regardless_of_case()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, "vm-1", personalTags: ["dev", "zeta"]), Ct);
        await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, "vm-2", personalTags: ["DEV", "alpha"]), Ct);

        // The latest-reporting agent's casing wins.
        Assert.Equal(" | alpha, DEV, zeta", TagsOf(await CardAsync(world.Emmz, change)));
    }

    [Fact]
    public async Task Tagging_answers_with_the_callers_own_personal_tags()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["dev"]), Ct);

        using (var byEmmz = await world.Emmz.PostApiAsync(ChangeTagEndpoints.TagsPath(change), new { add = new[] { bug } }, Ct))
        {
            Assert.Equal("bug | dev", TagsOf(await byEmmz.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }

        using var bySam = await world.Sam.PostApiAsync(ChangeTagEndpoints.TagsPath(change), new { add = new[] { bug } }, Ct);
        Assert.Equal("bug | ", TagsOf(await bySam.Content.ReadFromJsonAsync<JsonElement>(Ct)));
    }
}
