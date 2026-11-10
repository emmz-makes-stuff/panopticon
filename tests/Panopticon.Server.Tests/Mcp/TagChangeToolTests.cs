using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Mcp;

// spec tags: "Tagging a change over MCP", "Agents only use existing tags" (design D4).
public class TagChangeToolTests(PostgresFixture postgres, S3Fixture s3)
{
    private static readonly RepoRef _repo = new("github.com", "emmz", "panopticon");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<CallToolResult> TagAsync(McpClient client, string change, Guid project, string[]? add = null, string[]? remove = null) =>
        client.CallToolAsync(
            ContractsAssembly.TagChangeToolName,
            new TagChangeArguments(change, add, remove, project.ToString(), _repo).ToToolArguments(),
            cancellationToken: Ct).AsTask();

    private static TagChangeResult ResultOf(CallToolResult called)
    {
        Assert.True(called.IsError is not true, TagWorld.TextOf(called));
        return called.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.TagChangeResult)!;
    }

    private static async Task<List<string>> TagsOnAsync(TagWorld world, Guid change)
    {
        await using var db = world.Factory.NewDb();
        return await db.ChangeProjectTags.Where(t => t.ChangeId == change).Select(t => t.Tag.Key).OrderBy(k => k).ToListAsync(Ct);
    }

    private static async Task<McpClient> AgentOfAsync(TagWorld world, string email) =>
        await world.Factory.CreateMcpClientAsync(await world.Factory.CreateApiTokenAsync(email, cancellationToken: Ct), Ct);

    [Fact]
    public async Task The_endpoint_offers_tag_change()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);

        var tools = await world.Reporter.ListToolsAsync(cancellationToken: Ct);

        Assert.Contains(tools, t => t.Name == ContractsAssembly.TagChangeToolName);
    }

    [Fact]
    public async Task An_editors_agent_tags_its_change_and_gets_the_changes_tags()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.AddPanTagAsync("bug", Ct);
        await world.AddPanTagAsync("area: onboarding", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);
        await using var sams = await AgentOfAsync(world, TagWorld.SamEmail);

        var called = await TagAsync(sams, "Walking-Skeleton", world.Pan, add: ["BUG"]);

        var result = ResultOf(called);
        Assert.Equal((change.ToString(), "walking-skeleton", "PAN"), (result.ChangeId, result.Change, result.Code));
        Assert.Equal(["bug"], result.Tags);
        Assert.Equal("walking-skeleton now has PAN/bug.", TagWorld.TextOf(called));
        Assert.Equal(["bug"], await TagsOnAsync(world, change));
    }

    [Fact]
    public async Task Adding_and_removing_at_once()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.AddPanTagAsync("bug", Ct);
        await world.AddPanTagAsync("area: onboarding", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);
        ResultOf(await TagAsync(world.Reporter, "walking-skeleton", world.Pan, add: ["bug"]));

        var result = ResultOf(await TagAsync(world.Reporter, "walking-skeleton", world.Pan, add: ["area: onboarding"], remove: ["bug"]));

        Assert.Equal(["area: onboarding"], result.Tags);
        Assert.Equal(["area"], await TagsOnAsync(world, change));
    }

    [Fact]
    public async Task A_viewers_agent_is_refused_and_nothing_changes()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.AddPanTagAsync("bug", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);
        await using var kits = await AgentOfAsync(world, TagWorld.KitEmail);

        var called = await TagAsync(kits, "walking-skeleton", world.Pan, add: ["bug"]);

        Assert.True(called.IsError);
        Assert.Contains("Your role in PAN is viewer, and a viewer can't tag changes.", TagWorld.TextOf(called), StringComparison.Ordinal);
        Assert.Empty(await TagsOnAsync(world, change));
    }

    [Fact]
    public async Task An_unknown_tag_is_refused_listing_the_Projects_tags_and_nothing_changes()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.AddPanTagAsync("bug", Ct);
        await world.AddPanTagAsync("area: onboarding", Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        var called = await TagAsync(world.Reporter, "walking-skeleton", world.Pan, add: ["area: onboarding", "bugg"]);

        Assert.True(called.IsError);
        var text = TagWorld.TextOf(called);
        Assert.Contains("PAN has no tag \"bugg\"", text, StringComparison.Ordinal);
        Assert.Contains("Its tags are: area: onboarding, bug.", text, StringComparison.Ordinal);
        Assert.Empty(await TagsOnAsync(world, change));
    }

    [Fact]
    public async Task No_such_change_in_the_Project()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.AddPanTagAsync("bug", Ct);

        var called = await TagAsync(world.Reporter, "nope", world.Pan, add: ["bug"]);

        Assert.True(called.IsError);
        Assert.Contains("There's no change \"nope\" in PAN for github.com/emmz/panopticon.", TagWorld.TextOf(called), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Project_you_dont_belong_to_is_refused()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var called = await TagAsync(world.Reporter, "walking-skeleton", Guid.NewGuid(), add: ["bug"]);

        Assert.True(called.IsError);
        Assert.Contains("You aren't a member of a Project with id", TagWorld.TextOf(called), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_active_change_it_tags_the_latest_archived_one()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.AddPanTagAsync("bug", Ct);
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan), Ct);
        var archived = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan, archived: true), Ct);

        var result = ResultOf(await TagAsync(world.Reporter, "team-auth", world.Pan, add: ["bug"]));

        Assert.Equal(archived.ToString(), result.ChangeId);
        Assert.Equal(["bug"], await TagsOnAsync(world, archived));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("key:")]
    public async Task A_malformed_tag_is_the_agents_error(string tag)
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        var called = await TagAsync(world.Reporter, "walking-skeleton", world.Pan, add: [tag]);

        Assert.True(called.IsError);
        Assert.Contains($"\"{tag}\"", TagWorld.TextOf(called), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bootstrap_reporter_may_tag_any_change()
    {
        const string bootstrap = "dev-bootstrap-token";
        await using var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrap)
        {
            Environment = "Development",
        };
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        using (await browser.PostApiAsync(Panopticon.Server.Projects.ProjectTagEndpoints.TagsPath(pan), new { tag = "bug" }, Ct))
        {
        }

        await using var client = await factory.CreateMcpClientAsync(bootstrap, Ct);
        await TagWorld.ReportAsync(client, TagWorld.Report("walking-skeleton", pan), Ct);

        var result = ResultOf(await TagAsync(client, "walking-skeleton", pan, add: ["bug"]));

        Assert.Equal(["bug"], result.Tags);
    }
}
