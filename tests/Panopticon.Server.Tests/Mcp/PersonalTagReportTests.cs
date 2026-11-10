using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using Panopticon.Contracts;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Mcp;

// spec progress-reporting "Personal tags in reports"; spec tags "Personal tags on a change" (design D5).
public class PersonalTagReportTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A member's personal tags on a change: the union of their agents' sets, in canonical text.</summary>
    private static async Task<List<string>> TagsOfAsync(TagWorld world, Guid change, string email)
    {
        await using var db = world.Factory.NewDb();
        var memberId = await db.Members.Where(m => m.EmailKey == email).Select(m => m.Id).SingleAsync(Ct);
        var rows = await db.ChangeAgentTags
            .Where(t => t.ChangeId == change && db.Agents.Any(a => a.Id == t.AgentId && a.MemberId == memberId))
            .Select(t => new { t.Key, t.Value })
            .ToListAsync(Ct);
        return [.. rows.Select(r => new Tag(r.Key, r.Value)).Distinct().Select(t => t.Text).Order(StringComparer.Ordinal)];
    }

    private static Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(McpClient client, ReportProgressArguments report) =>
        client.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct).AsTask();

    [Fact]
    public async Task Two_machines_tags_are_combined_and_a_removed_tag_leaves()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, "vm-1", personalTags: ["dev"]), Ct);
        await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, "laptop", personalTags: ["urgent"]), Ct);

        Assert.Equal(["dev", "urgent"], await TagsOfAsync(world, change, PanopticonFactory.MemberEmail));

        await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, "vm-1", personalTags: []), Ct);

        Assert.Equal(["urgent"], await TagsOfAsync(world, change, PanopticonFactory.MemberEmail));
    }

    [Fact]
    public async Task A_report_without_the_field_keeps_the_agents_tags()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["dev"]), Ct);

        await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        Assert.Equal(["dev"], await TagsOfAsync(world, change, PanopticonFactory.MemberEmail));
    }

    [Fact]
    public async Task Each_members_tags_stay_their_own()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var change = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["dev"]), Ct);
        await using var sams = await world.Factory.CreateMcpClientAsync(await world.Factory.CreateApiTokenAsync(TagWorld.SamEmail, cancellationToken: Ct), Ct);
        await TagWorld.ReportAsync(sams, TagWorld.Report("walking-skeleton", world.Pan, "sams-vm", personalTags: ["secret"]), Ct);

        Assert.Equal(["dev"], await TagsOfAsync(world, change, PanopticonFactory.MemberEmail));
        Assert.Equal(["secret"], await TagsOfAsync(world, change, TagWorld.SamEmail));
    }

    [Theory]
    [InlineData("has space", "\"has space\"")]
    [InlineData("key:", "\"key:\"")]
    public async Task A_malformed_tag_refuses_the_report_and_records_nothing(string tag, string named)
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);

        var called = await CallAsync(world.Reporter, TagWorld.Report("walking-skeleton", world.Pan, personalTags: ["dev", tag]));

        Assert.True(called.IsError);
        Assert.Contains(named, TagWorld.TextOf(called), StringComparison.Ordinal);
        await using var db = world.Factory.NewDb();
        Assert.Equal(0, await db.Changes.CountAsync(Ct));
        Assert.Equal(0, await db.ReportEvents.CountAsync(Ct));
    }

    [Fact]
    public async Task More_than_20_refuses_the_report()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);

        var called = await CallAsync(world.Reporter, TagWorld.Report("walking-skeleton", world.Pan, personalTags: [.. Enumerable.Range(1, 21).Select(i => $"t{i}")]));

        Assert.True(called.IsError);
        Assert.Contains("at most 20", TagWorld.TextOf(called), StringComparison.Ordinal);
        await using var db = world.Factory.NewDb();
        Assert.Equal(0, await db.Changes.CountAsync(Ct));
    }

    [Fact]
    public async Task An_archived_change_keeps_its_tags_as_they_were()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan, personalTags: ["dev"]), Ct);
        var archived = await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan, archived: true, personalTags: ["dev"]), Ct);

        // The watcher and the agent both send the archived report; the repeat changes nothing, tags included.
        await world.ReportChangeAsync(TagWorld.Report("team-auth", world.Pan, archived: true, personalTags: ["other"]), Ct);

        Assert.Equal(["dev"], await TagsOfAsync(world, archived, PanopticonFactory.MemberEmail));
    }

    [Fact]
    public async Task A_personal_change_keeps_its_tags_when_linking_moves_it_into_a_Project()
    {
        await using var world = await TagWorld.CreateAsync(postgres, s3, Ct);
        var personal = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", project: null, personalTags: ["dev"]), Ct);

        var linked = await world.ReportChangeAsync(TagWorld.Report("walking-skeleton", world.Pan), Ct);

        Assert.Equal(personal, linked);
        Assert.Equal(["dev"], await TagsOfAsync(world, linked, PanopticonFactory.MemberEmail));
    }
}
