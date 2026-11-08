using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Changes;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// Spec cli-bridge: "Change documents read locally", "Document size limits" and "Archived change reported", through the
// real bridge host and an in-process server (design D2, D3).
public class BridgeDocumentTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeSettings Settings(string workingDirectory, string token) =>
        new("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true);

    [Fact]
    public async Task The_whole_folder_reaches_the_server()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        repo.WriteFile("openspec/changes/team-auth/proposal.md", "# Proposal\n");
        repo.WriteFile("openspec/changes/team-auth/specs/cli-bridge/spec.md", "# Spec\n");
        repo.WriteFile("openspec/changes/team-auth/diagram.png", "png");
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);

        var result = await bridge.ReportAsync("team-auth", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        await using var db = server.NewDb();
        Assert.Equal(
            ["proposal.md", "specs/cli-bridge/spec.md", "tasks.md"],
            await db.ChangeDocuments.OrderBy(d => d.Path).Select(d => d.Path).ToListAsync(Ct));
    }

    [Fact]
    public async Task A_folder_over_the_limit_is_a_tool_error_and_nothing_is_sent()
    {
        var requests = 0;
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        repo.WriteFile("openspec/changes/team-auth/devlog/huge.md", new string('a', 300 * 1024));
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, PanopticonFactory.Token),
            () => new HttpClient(new CountingHandler(() => requests++)),
            "claude-code",
            Ct);

        var result = await bridge.ReportAsync("team-auth", null, Ct);

        Assert.True(result.IsError);
        Assert.Contains("'devlog/huge.md'", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Contains("256 KB", BridgeHarness.Text(result), StringComparison.Ordinal);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task A_report_after_archive_lands_the_change_in_history()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);

        // /opsx:archive moves the folder; the agent then reports the same name once more.
        var archiveRoot = repo.CreateSubdirectory(Path.Combine("openspec", "changes", "archive"));
        Directory.Move(Path.Combine(repo.Root, "openspec", "changes", "team-auth"), Path.Combine(archiveRoot, "2026-11-08-team-auth"));
        repo.WriteFile("openspec/changes/archive/2026-11-08-team-auth/design.md", "# Final design\n");
        var result = await bridge.ReportAsync("team-auth", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        Assert.Equal("Overall 1/2 · section 1 Setup 1/2 · archived as 2026-11-08-team-auth", BridgeHarness.Text(result));
        Assert.Equal("2026-11-08-team-auth", result.StructuredContent?.GetProperty("archived").GetString());
        using var http = await server.CreateSignedInClientAsync(cancellationToken: Ct);
        var history = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, Ct);
        var archived = Assert.Single(history.GetProperty("changes").EnumerateArray());
        Assert.Equal(("team-auth", "2026-11-08-team-auth"), (archived.GetProperty("change").GetString(), archived.GetProperty("archiveFolder").GetString()));
        await using var db = server.NewDb();
        Assert.Contains("design.md", await db.ChangeDocuments.Select(d => d.Path).ToListAsync(Ct));
        Assert.Contains("archived as 2026-11-08-team-auth", bridge.Log, StringComparison.Ordinal);

        // Reporting it again changes nothing ("Later reports don't change it").
        var again = await bridge.ReportAsync("team-auth", null, Ct);
        Assert.Equal(BridgeHarness.Text(result), BridgeHarness.Text(again));
        Assert.Equal(2, await db.ReportEvents.CountAsync(Ct));
    }

    private sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }
}
