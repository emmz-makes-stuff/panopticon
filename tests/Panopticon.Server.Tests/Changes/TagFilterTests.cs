using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Server.Changes;
using Panopticon.Server.Metrics;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// spec change-filters: "Filter by tags" and "Tag filter choices" (history); spec change-metrics: "Metrics by tag" and
// "Metrics by a personal tag" (design D7).
public class TagFilterTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Query(params string[] tags) => string.Join('&', tags.Select(t => $"tag={Uri.EscapeDataString(t)}"));

    private static async Task<List<string>> HistoryAsync(HttpClient http, params string[] tags) =>
        [.. (await http.GetFromJsonAsync<JsonElement>($"{ChangeEndpoints.HistoryPath}?{Query(tags)}", Ct)).GetProperty("changes")
            .EnumerateArray().Select(c => c.GetProperty("change").GetString()!).Order(StringComparer.Ordinal)];

    private static async Task<int> MetricsCountAsync(HttpClient http, params string[] tags) =>
        (await http.GetFromJsonAsync<JsonElement>($"{MetricsEndpoints.MetricsPath}?{Query(tags)}", Ct)).GetProperty("changes").GetInt32();

    private static async Task<List<string>> ChoicesAsync(HttpClient http) =>
        [.. (await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryFiltersPath, Ct)).GetProperty("tags").EnumerateArray()
            .Select(t =>
            {
                var code = t.GetProperty("project").ValueKind == JsonValueKind.Null ? string.Empty : $"{t.GetProperty("project").GetProperty("code").GetString()}/";
                var text = t.GetProperty("value").GetString() is { } value ? $"{t.GetProperty("key").GetString()}: {value}" : t.GetProperty("key").GetString();
                return $"{code}{text} = {t.GetProperty("filter").GetString()}";
            })];

    /// <summary>Reports a change as Emmz with personal tags, puts PAN tags on it, and archives it.</summary>
    private static async Task<Guid> ArchivedAsync(TagWorld world, string change, string[] personalTags, params Guid[] projectTags)
    {
        var id = await world.ReportChangeAsync(TagWorld.Report(change, world.Pan, personalTags: personalTags), Ct);
        if (projectTags.Length > 0)
        {
            using var tagged = await world.Emmz.PostApiAsync(ChangeTagEndpoints.TagsPath(id), new { add = projectTags }, Ct);
            Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        }

        await world.ReportChangeAsync(TagWorld.Report(change, world.Pan, archived: true), Ct);
        return id;
    }

    [Fact]
    public async Task A_change_matches_only_when_it_has_every_chosen_tag()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var csharp = await world.AddPanTagAsync("language: csharp", Ct);
        await ArchivedAsync(world, "both", [], bug, csharp);
        await ArchivedAsync(world, "bug-only", [], bug);
        await ArchivedAsync(world, "bug-and-dev", ["dev"], bug);
        await ArchivedAsync(world, "neither", ["dev"]);

        Assert.Equal(["both"], await HistoryAsync(world.Emmz, $"t:{bug}", $"t:{csharp}"));
        Assert.Equal(["bug-and-dev"], await HistoryAsync(world.Emmz, $"t:{bug}", "p:dev"));
        Assert.Equal(["both", "bug-and-dev", "bug-only"], await HistoryAsync(world.Emmz, $"t:{bug}"));
        Assert.Equal(1, await MetricsCountAsync(world.Emmz, $"t:{bug}", $"t:{csharp}"));
        Assert.Equal(3, await MetricsCountAsync(world.Emmz, $"t:{bug}"));
    }

    [Fact]
    public async Task A_key_alone_matches_any_value()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var csharp = await world.AddPanTagAsync("language: csharp", Ct);
        var rust = await world.AddPanTagAsync("Language: rust", Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        await ArchivedAsync(world, "in-csharp", ["area: cli"], csharp);
        await ArchivedAsync(world, "in-rust", ["Area: Web"], rust);
        await ArchivedAsync(world, "no-language", ["area"], bug);
        await ArchivedAsync(world, "untagged", []);

        Assert.Equal(["in-csharp", "in-rust"], await HistoryAsync(world.Emmz, $"k:{world.Pan}:LANGUAGE"));
        Assert.Equal(["in-csharp", "in-rust", "no-language"], await HistoryAsync(world.Emmz, "p:area"));
        Assert.Equal(["in-rust"], await HistoryAsync(world.Emmz, "p:area:web"));
        Assert.Empty(await HistoryAsync(world.Emmz, $"k:{Guid.NewGuid()}:language"));
    }

    [Fact]
    public async Task A_personal_tag_matches_only_the_viewers_own_agents()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await ArchivedAsync(world, "emmz-dev", ["dev"]);
        await using (var sams = await world.Factory.CreateMcpClientAsync(await world.Factory.CreateApiTokenAsync(TagWorld.SamEmail, cancellationToken: Ct), Ct))
        {
            await TagWorld.ReportAsync(sams, TagWorld.Report("sam-dev", world.Pan, "sams-laptop", personalTags: ["dev"]), Ct);
            await TagWorld.ReportAsync(sams, TagWorld.Report("sam-dev", world.Pan, "sams-laptop", archived: true), Ct);
        }

        Assert.Equal(["emmz-dev"], await HistoryAsync(world.Emmz, "p:dev"));
        Assert.Equal(["sam-dev"], await HistoryAsync(world.Sam, "p:dev"));
        Assert.Empty(await HistoryAsync(world.Kit, "p:dev"));
        Assert.Equal(1, await MetricsCountAsync(world.Emmz, "p:dev"));
        Assert.Equal(0, await MetricsCountAsync(world.Kit, "p:dev"));
    }

    [Fact]
    public async Task A_Project_tag_filter_survives_a_rename_and_matches_nothing_once_deleted()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        await ArchivedAsync(world, "buggy", [], bug);
        using (var renamed = await world.Emmz.SendApiAsync(HttpMethod.Patch, ProjectTagEndpoints.TagPath(world.Pan, bug), new { tag = "defect" }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        }

        Assert.Equal(["buggy"], await HistoryAsync(world.Emmz, $"t:{bug}"));

        using (var deleted = await world.Emmz.SendApiAsync(HttpMethod.Delete, ProjectTagEndpoints.TagPath(world.Pan, bug), null, Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        Assert.Empty(await HistoryAsync(world.Emmz, $"t:{bug}"));
    }

    [Theory]
    [InlineData("x:1")]
    [InlineData("t:not-a-guid")]
    [InlineData("k:not-a-guid:language")]
    [InlineData("k:00000000-0000-0000-0000-000000000001")]
    [InlineData("k:00000000-0000-0000-0000-000000000001:language: csharp")]
    [InlineData("p:")]
    [InlineData("p:bad key")]
    [InlineData("p:dev:")]
    [InlineData("dev")]
    public async Task A_malformed_tag_is_a_400_naming_the_parameter(string tag)
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);

        foreach (var path in new[] { ChangeEndpoints.HistoryPath, MetricsEndpoints.MetricsPath })
        {
            using var response = await world.Emmz.GetAsync($"{path}?{Query(tag)}", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("tag must be", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task At_most_20_tags_can_be_chosen()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var twenty = Enumerable.Range(1, 20).Select(i => $"p:tag-{i}").ToArray();

        foreach (var path in new[] { ChangeEndpoints.HistoryPath, MetricsEndpoints.MetricsPath })
        {
            using (var ok = await world.Emmz.GetAsync($"{path}?{Query(twenty)}", Ct))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }

            using var tooMany = await world.Emmz.GetAsync($"{path}?{Query([.. twenty, "p:tag-21"])}", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
            Assert.Contains("tag can be given at most 20 times", await tooMany.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_choices_are_the_viewers_own_personal_tags_then_each_Projects_with_keys_alone()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var bug = await world.AddPanTagAsync("bug", Ct);
        var csharp = await world.AddPanTagAsync("language: csharp", Ct);
        await world.AddPanTagAsync("unused", Ct);
        await using (var sams = await world.Factory.CreateMcpClientAsync(await world.Factory.CreateApiTokenAsync(TagWorld.SamEmail, cancellationToken: Ct), Ct))
        {
            // Sam works on "one" too, before it's archived.
            await TagWorld.ReportAsync(sams, TagWorld.Report("one", world.Pan, "sams-laptop", personalTags: ["secret"]), Ct);
        }

        await ArchivedAsync(world, "one", ["dev", "language: csharp"], bug);
        await ArchivedAsync(world, "two", ["DEV", "language: rust"], csharp);

        // An active change's tags aren't the history's.
        await world.ReportChangeAsync(TagWorld.Report("still-going", world.Pan, personalTags: ["active-only"]), Ct);

        var emmzs = await ChoicesAsync(world.Emmz);
        Assert.Equal(
            [
                "dev = p:dev",
                "language = p:language",
                "language: csharp = p:language:csharp",
                "language: rust = p:language:rust",
                $"PAN/bug = t:{bug}",
                $"PAN/language = k:{world.Pan}:language",
                $"PAN/language: csharp = t:{csharp}",
            ],
            emmzs);
        Assert.Equal(["secret = p:secret", $"PAN/bug = t:{bug}", $"PAN/language = k:{world.Pan}:language", $"PAN/language: csharp = t:{csharp}"], await ChoicesAsync(world.Sam));
        Assert.Equal([$"PAN/bug = t:{bug}", $"PAN/language = k:{world.Pan}:language", $"PAN/language: csharp = t:{csharp}"], await ChoicesAsync(world.Kit));
    }
}
