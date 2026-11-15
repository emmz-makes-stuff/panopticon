using System.Text.Json;
using ModelContextProtocol.Protocol;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// projects D6; spec cli-bridge: "Listing Projects through the bridge" (Agent links a repo, No login, and a refusal),
// through the real bridge host and an in-process server.
public class ListProjectsBridgeTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeSettings Settings(string workingDirectory, string? token) =>
        new("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true);

    private static Task<CallToolResult> ListAsync(BridgeHarness bridge) =>
        bridge.Client.CallToolAsync(ContractsAssembly.ListProjectsToolName, cancellationToken: Ct).AsTask();

    [Fact]
    public async Task The_agent_gets_the_reporters_Projects_with_ids_names_codes_and_roles()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var browser = await server.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await browser.CreateProjectAsync("PAN", Ct);
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await ListAsync(bridge);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        var list = result.StructuredContent!.Value.Deserialize(ContractsJsonContext.Default.ListProjectsResult)!;
        Assert.Equal([new ProjectSummary(pan.ToString(), "PAN", "PAN", "owner")], list.Projects);
        Assert.Contains($"PAN · PAN · owner · id {pan}", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Contains(".panopticon.json", BridgeHarness.Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_login_it_names_the_token_variable_and_panopticon_login_and_keeps_serving()
    {
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, token: null), () => new HttpClient(), "claude-code", Ct);

        var result = await ListAsync(bridge);

        Assert.True(result.IsError);
        Assert.Contains("PANOPTICON_TOKEN", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Contains("panopticon login", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Equal(4, (await bridge.Client.ListToolsAsync(cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task A_refused_token_is_a_tool_error_that_doesnt_echo_it()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, "not-the-right-token"), server.CreateClient, "claude-code", Ct);

        var result = await ListAsync(bridge);

        Assert.True(result.IsError);
        var text = BridgeHarness.Text(result);
        Assert.Contains("rejected the token from PANOPTICON_TOKEN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-right-token", text, StringComparison.Ordinal);
    }
}
