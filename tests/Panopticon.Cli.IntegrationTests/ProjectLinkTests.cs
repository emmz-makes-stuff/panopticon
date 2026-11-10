using Microsoft.EntityFrameworkCore;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// projects D7; spec cli-bridge: "Project link file" (Linked repo, No link file), "Malformed link file" (Not a UUID, and
// invalid JSON), through the real bridge host and an in-process server.
public class ProjectLinkTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeSettings Settings(string workingDirectory, string token) =>
        new("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true);

    [Fact]
    public async Task A_linked_repos_report_lands_in_its_Project_and_other_fields_are_ignored()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await File.WriteAllTextAsync(
            Path.Combine(repo.Root, ProjectLink.FileName),
            $$"""{ "project": "{{pan}}", "tags": ["later"] }""",
            Ct);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        await using var db = server.NewDb();
        var change = await db.Changes.SingleAsync(Ct);
        Assert.Equal((pan, (Guid?)null), (change.ProjectId, change.OwnerMemberId));
    }

    [Fact]
    public async Task With_no_link_file_the_report_is_the_reporters_own()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        await using var db = server.NewDb();
        var change = await db.Changes.SingleAsync(Ct);
        Assert.Equal(((Guid?)null, (Guid?)await server.MemberIdAsync(cancellationToken: Ct)), (change.ProjectId, change.OwnerMemberId));
    }

    [Theory]
    [InlineData("""{ "project": "PAN" }""", "\"project\" must be a Project id from list_projects")]
    [InlineData("""{ "project": 42 }""", "\"project\" must be a Project id from list_projects")]
    [InlineData("""{ "name": "PAN" }""", "has no \"project\"")]
    [InlineData("""{ "project": null }""", "has no \"project\"")]
    [InlineData("""{ "project": """, "isn't valid JSON")]
    [InlineData("""["6f1c"]""", "isn't valid JSON")]
    public async Task A_malformed_link_file_is_a_tool_error_naming_it_and_nothing_is_sent(string content, string problem)
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ProjectLink.FileName), content, Ct);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("walking-skeleton", null, Ct);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.Contains(".panopticon.json", text, StringComparison.Ordinal);
        Assert.Contains(problem, text, StringComparison.Ordinal);
        await using (var db = server.NewDb())
        {
            Assert.Equal(0, await db.Changes.CountAsync(Ct));
        }

        // The bridge keeps serving: fix the file, and the next report goes through.
        File.Delete(Path.Combine(repo.Root, ProjectLink.FileName));
        Assert.True((await bridge.ReportAsync("walking-skeleton", null, Ct)).IsError is not true);
    }
}
