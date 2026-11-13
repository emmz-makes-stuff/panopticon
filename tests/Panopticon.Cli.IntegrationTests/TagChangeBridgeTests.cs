using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// tags D8; spec cli-bridge: "Tagging through the bridge" (An agent tags its change, No link file, Refused by the
// server), through the real bridge host and an in-process server.
public class TagChangeBridgeTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeSettings Settings(string workingDirectory, string token) =>
        new("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true);

    private static Task<CallToolResult> TagAsync(BridgeHarness bridge, List<string>? add = null, List<string>? remove = null, Guid? project = null)
    {
        var arguments = new Dictionary<string, object?> { ["change"] = "walking-skeleton", ["add"] = add, ["remove"] = remove };
        if (project is { } id)
        {
            arguments["project"] = id.ToString();
        }

        return bridge.Client.CallToolAsync(ContractsAssembly.TagChangeToolName, arguments, cancellationToken: Ct).AsTask();
    }

    /// <summary>A server with <c>PAN</c> (tags bug and area: cli), and a repo linked to it with a reported change.</summary>
    private async Task<World> WorldAsync(bool link = true)
    {
        var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        foreach (var tag in new[] { "bug", "area: cli" })
        {
            using var added = await browser.PostApiAsync(ProjectTagEndpoints.TagsPath(pan), new { tag }, Ct);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", Tasks);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ProjectLink.FileName), $$"""{ "project": "{{pan}}" }""", Ct);
        var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);
        var reported = await bridge.ReportAsync("walking-skeleton", null, Ct);
        Assert.True(reported.IsError is not true, BridgeHarness.Text(reported));
        if (!link)
        {
            File.Delete(Path.Combine(repo.Root, ProjectLink.FileName));
        }

        return new World(server, repo, bridge, pan);
    }

    private sealed record World(PanopticonFactory Server, TempGitRepo Repo, BridgeHarness Bridge, Guid Pan) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            Repo.Dispose();
            await Server.DisposeAsync();
        }
    }

    [Fact]
    public async Task In_a_linked_repo_the_agent_tags_its_change_by_name()
    {
        await using var world = await WorldAsync();
        var bridge = world.Bridge;

        var result = await TagAsync(bridge, add: ["bug", "Area: CLI"]);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        var tagged = result.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.TagChangeResult)!;
        Assert.Equal(("walking-skeleton", "PAN"), (tagged.Change, tagged.Code));
        Assert.Equal(["area: cli", "bug"], tagged.Tags);
        Assert.Equal("walking-skeleton now has PAN/area: cli, PAN/bug.", BridgeHarness.Text(result));
        await using var db = world.Server.NewDb();
        Assert.Equal(2, await db.ChangeProjectTags.CountAsync(t => t.Tag.ProjectId == world.Pan, Ct));

        var removed = await TagAsync(bridge, remove: ["bug"]);
        Assert.Equal(["area: cli"], removed.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.TagChangeResult)!.Tags);
    }

    [Fact]
    public async Task With_no_link_file_it_says_the_repo_isnt_linked_and_keeps_serving()
    {
        await using var world = await WorldAsync(link: false);
        var bridge = world.Bridge;

        var result = await TagAsync(bridge, add: ["bug"]);

        Assert.True(result.IsError);
        Assert.Contains("isn't linked to a Project", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Equal(4, (await bridge.Client.ListToolsAsync(cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task A_Project_id_given_by_the_agent_is_used_with_no_link_file()
    {
        await using var world = await WorldAsync(link: false);
        var bridge = world.Bridge;

        var result = await TagAsync(bridge, add: ["bug"], project: world.Pan);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Equal("walking-skeleton now has PAN/bug.", BridgeHarness.Text(result));
    }

    [Fact]
    public async Task A_server_refusal_comes_back_with_the_Projects_tags_and_the_bridge_keeps_serving()
    {
        await using var world = await WorldAsync();
        var bridge = world.Bridge;

        var result = await TagAsync(bridge, add: ["no-such-tag"]);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.Contains("The Panopticon server refused the tag change", text, StringComparison.Ordinal);
        Assert.Contains("no-such-tag", text, StringComparison.Ordinal);
        Assert.Contains("area: cli", text, StringComparison.Ordinal);
        Assert.Contains("bug", text, StringComparison.Ordinal);
        await using (var db = world.Server.NewDb())
        {
            Assert.False(await db.ChangeProjectTags.AnyAsync(Ct));
        }

        Assert.True((await TagAsync(bridge, add: ["bug"])).IsError is not true);
    }
}
