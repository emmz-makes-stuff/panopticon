using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Mcp;

// projects D6; spec projects: "Listing Projects over MCP" (An agent finds the Project to link, and the bootstrap reporter).
public class ListProjectsToolTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_endpoint_offers_list_projects()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        await using var client = await factory.CreateMcpClientAsync(Ct);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Contains(tools, t => t.Name == ContractsAssembly.ListProjectsToolName);
    }

    [Fact]
    public async Task A_member_gets_their_Projects_with_their_roles_and_no_others()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        var acme = await browser.CreateProjectAsync("ACME", Ct);
        var zed = await browser.CreateProjectAsync("ZED", Ct);
        await using (var db = factory.NewDb())
        {
            // She's an editor of ACME, and ZED isn't hers at all.
            await db.ProjectMembers.Where(m => m.ProjectId == acme).ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, "editor"), Ct);
            await db.ProjectMembers.Where(m => m.ProjectId == zed).ExecuteDeleteAsync(Ct);
        }

        await using var client = await factory.CreateMcpClientAsync(Ct);

        var (result, text) = await ListAsync(client);

        Assert.Equal(
            [new ProjectSummary(acme.ToString(), "ACME", "ACME", "editor"), new ProjectSummary(pan.ToString(), "PAN", "PAN", "owner")],
            result.Projects);
        Assert.Contains($"PAN · PAN · owner · id {pan}", text, StringComparison.Ordinal);
        Assert.Contains(".panopticon.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ZED", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_in_no_Project_gets_an_empty_list_and_is_told_so()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var acme = await browser.CreateProjectAsync("ACME", Ct);
        await using (var db = factory.NewDb())
        {
            await db.ProjectMembers.Where(m => m.ProjectId == acme).ExecuteDeleteAsync(Ct);
        }

        await using var client = await factory.CreateMcpClientAsync(Ct);

        var (result, text) = await ListAsync(client);

        Assert.Empty(result.Projects);
        Assert.Contains("aren't a member of any Project", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bootstrap_reporter_gets_every_Project_as_a_viewer()
    {
        const string bootstrap = "dev-bootstrap-token";
        await using var factory = new PanopticonFactory(
            await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrap)
        {
            Environment = "Development",
        };
        using var browser = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        var acme = await browser.CreateProjectAsync("ACME", Ct);
        await using var client = await factory.CreateMcpClientAsync(bootstrap, Ct);

        var (result, _) = await ListAsync(client);

        Assert.Equal(
            [new ProjectSummary(acme.ToString(), "ACME", "ACME", "viewer"), new ProjectSummary(pan.ToString(), "PAN", "PAN", "viewer")],
            result.Projects);
    }

    private static async Task<(ListProjectsResult Result, string Text)> ListAsync(McpClient client)
    {
        var called = await client.CallToolAsync(ContractsAssembly.ListProjectsToolName, cancellationToken: Ct);
        Assert.True(called.IsError is not true);
        var result = called.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.ListProjectsResult)!;
        return (result, string.Join("\n", called.Content.OfType<TextContentBlock>().Select(b => b.Text)));
    }
}
