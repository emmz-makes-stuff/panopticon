using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Contracts;
using Panopticon.Server.Changes;
using Panopticon.Server.Metrics;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// projects D4 (Emmz's 2026-11-10 decision); spec change-history "A copy retired by linking", change-metrics "Retired
// copies don't count".
public class RetiredCopyTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string KitEmail = "kit@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_retired_copy_stays_in_its_owners_history_linked_to_the_Project_and_leaves_the_metrics()
    {
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        await emmz.InviteToProjectAsync(pan, KitEmail, "viewer", Ct);
        using var kit = await factory.CreateSignedInClientAsync(KitEmail, Ct);
        var kitToken = await factory.CreateApiTokenAsync(KitEmail, cancellationToken: Ct);
        var emmzToken = await factory.MemberTokenAsync(Ct);

        // Kit links first; Emmz has a personal cleanup, then links too, which retires hers. Then PAN's is archived.
        await ReportAsync(factory, kitToken, pan);
        await ReportAsync(factory, emmzToken, project: null);
        await ReportAsync(factory, emmzToken, pan);
        await ReportAsync(factory, emmzToken, pan, archive: "2026-11-10-cleanup");

        var history = (await emmz.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, Ct)).GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        var retired = Assert.Single(history, h => h.GetProperty("personal").GetBoolean());
        Assert.Equal(JsonValueKind.Null, retired.GetProperty("archiveFolder").ValueKind);
        var linkedTo = retired.GetProperty("linkedTo");
        Assert.Equal((pan, "PAN"), (linkedTo.GetProperty("id").GetGuid(), linkedTo.GetProperty("code").GetString()));
        var archived = Assert.Single(history, h => !h.GetProperty("personal").GetBoolean());
        Assert.Equal("2026-11-10-cleanup", archived.GetProperty("archiveFolder").GetString());
        Assert.False(archived.TryGetProperty("linkedTo", out var none) && none.ValueKind != JsonValueKind.Null);

        // One finished change, PAN's, for Emmz and Kit alike; the copy is Emmz's alone, and isn't counted.
        Assert.Equal(1, (await emmz.GetFromJsonAsync<JsonElement>(MetricsEndpoints.MetricsPath, Ct)).GetProperty("changes").GetInt32());
        Assert.Equal(1, (await kit.GetFromJsonAsync<JsonElement>(MetricsEndpoints.MetricsPath, Ct)).GetProperty("changes").GetInt32());
        Assert.Single((await kit.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, Ct)).GetProperty("changes").EnumerateArray());
    }

    private static async Task ReportAsync(PanopticonFactory factory, string token, Guid? project, string? archive = null)
    {
        await using var client = await factory.CreateMcpClientAsync(token, Ct);
        var report = new ReportProgressArguments(
            new RepoRef("github.com", "emmz", "panopticon"), "cleanup", new AgentRef("claude-code", "vm-1"), "## 1. Work\n- [x] 1.1\n")
        {
            Project = project?.ToString(),
            Archive = archive,
        };
        var result = await client.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: Ct);
        Assert.True(result.IsError is not true, string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text)));
    }
}
