using Microsoft.EntityFrameworkCore;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// Spec cli-bridge: "Watching for archives" (design D4), through the real bridge host, a temp git repo, and an
// in-process server. The watcher debounces for a second, so each wait allows several.
public class ArchiveWatcherTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n";
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeSettings Settings(string workingDirectory, string token) =>
        new("http://localhost/", token, workingDirectory, "vm-1", TokenFromEnvironment: true);

    [Fact]
    public async Task Archive_while_the_bridge_runs_is_reported_by_the_bridge_itself()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);

        Archive(repo, "team-auth", "2026-11-08-team-auth");

        var change = await EventuallyAsync(server, async db => await db.Changes.SingleOrDefaultAsync(c => c.ArchiveFolder != null, Ct));
        Assert.Equal(("team-auth", "2026-11-08-team-auth"), (change.Name, change.ArchiveFolder));
        await using var db = server.NewDb();
        var lastEvent = await db.ReportEvents.OrderByDescending(e => e.Id).FirstAsync(Ct);
        var agent = await db.Agents.SingleAsync(a => a.Id == lastEvent.AgentId, Ct);
        Assert.Equal(("claude-code", "vm-1"), (agent.ClientName, agent.MachineName));
        Assert.Contains("archived as 2026-11-08-team-auth", bridge.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreported_change_archived_is_left_alone()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        repo.WriteTasks("other-change", Tasks);
        await using var bridge = await BridgeHarness.StartAsync(Settings(repo.Root, await server.MemberTokenAsync(Ct)), server.CreateClient, "claude-code", Ct);
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);

        Archive(repo, "other-change", "2026-11-08-other-change");
        await Task.Delay(ArchiveWatcher.Debounce * 4, Ct);

        await using var db = server.NewDb();
        var change = await db.Changes.SingleAsync(Ct);
        Assert.Equal(("team-auth", null), (change.Name, change.ArchiveFolder));
        Assert.Equal(1, await db.ReportEvents.CountAsync(Ct));
    }

    [Fact]
    public async Task Server_down_at_archive_time_is_logged_and_the_next_report_archives_it()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        var down = false;
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, await server.MemberTokenAsync(Ct)),
            () => down ? new HttpClient(new UnreachableHandler()) : server.CreateClient(),
            "claude-code",
            Ct);
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);

        down = true;
        Archive(repo, "team-auth", "2026-11-08-team-auth");
        await EventuallyAsync(() => bridge.Log.Contains("Report for change team-auth failed", StringComparison.Ordinal));

        // The bridge keeps serving, and the agent's next report finds the archive.
        Assert.Equal(2, (await bridge.Client.ListToolsAsync(cancellationToken: Ct)).Count);
        down = false;
        var result = await bridge.ReportAsync("team-auth", null, Ct);

        Assert.True(result.IsError is not true, BridgeHarness.Text(result));
        await using var db = server.NewDb();
        Assert.Equal("2026-11-08-team-auth", (await db.Changes.SingleAsync(Ct)).ArchiveFolder);
    }

    // Spec cli-bridge: "Watching for archives", Server unreachable at archive time (review fix 8.1).
    [Fact]
    public async Task Server_down_at_archive_time_is_retried_by_the_watcher_itself()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        var down = false;
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, await server.MemberTokenAsync(Ct)) with { ArchiveRetryInterval = TimeSpan.FromSeconds(1) },
            () => down ? new HttpClient(new UnreachableHandler()) : server.CreateClient(),
            "claude-code",
            Ct);
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);

        down = true;
        Archive(repo, "team-auth", "2026-11-08-team-auth");
        await EventuallyAsync(() => bridge.Log.Contains("Will retry the archived report", StringComparison.Ordinal));

        // No folder event and no tool call from here: only the retry can send it.
        down = false;
        var change = await EventuallyAsync(server, async db => await db.Changes.SingleOrDefaultAsync(c => c.ArchiveFolder != null, Ct));
        Assert.Equal("2026-11-08-team-auth", change.ArchiveFolder);
    }

    // Spec cli-bridge: "One report at a time per change", Report in flight at archive time.
    [Fact]
    public async Task A_report_in_flight_at_archive_time_is_sent_before_the_archived_one()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("team-auth", Tasks);
        var hold = new HoldNextRequest();
        await using var bridge = await BridgeHarness.StartAsync(
            Settings(repo.Root, await server.MemberTokenAsync(Ct)),
            () => server.CreateDefaultClient(new HoldingHandler(hold)),
            "claude-code",
            Ct);
        Assert.True((await bridge.ReportAsync("team-auth", null, Ct)).IsError is not true);

        // The agent's next report reads the active folder, then is held on its way to the server.
        hold.Arm();
        var inFlight = bridge.ReportAsync("team-auth", 1, Ct);
        await hold.Held.WaitAsync(_patience, Ct);
        Archive(repo, "team-auth", "2026-11-08-team-auth");

        // Long enough for the watcher to have sent its archived report, had it not waited.
        await Task.Delay(ArchiveWatcher.Debounce * 4, Ct);
        hold.Release();
        Assert.True((await inFlight).IsError is not true);

        var change = await EventuallyAsync(server, async db => await db.Changes.SingleOrDefaultAsync(c => c.ArchiveFolder != null, Ct));
        Assert.Equal("2026-11-08-team-auth", change.ArchiveFolder);
        await using var db = server.NewDb();
        Assert.Equal(1, await db.Changes.CountAsync(Ct));
    }

    private static void Archive(TempGitRepo repo, string change, string folder)
    {
        var archiveRoot = repo.CreateSubdirectory(Path.Combine("openspec", "changes", "archive"));
        Directory.Move(Path.Combine(repo.Root, "openspec", "changes", change), Path.Combine(archiveRoot, folder));
    }

    private static async Task<T> EventuallyAsync<T>(PanopticonFactory server, Func<Panopticon.Server.Data.PanopticonDb, Task<T?>> probe)
        where T : class
    {
        var deadline = DateTime.UtcNow + _patience;
        while (true)
        {
            await using (var db = server.NewDb())
            {
                if (await probe(db) is { } found)
                {
                    return found;
                }
            }

            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the watcher's report.");
            await Task.Delay(100, Ct);
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the watcher.");
            await Task.Delay(100, Ct);
        }
    }

    /// <summary>When armed, holds the next request sent until released; every other request goes straight through.</summary>
    private sealed class HoldNextRequest
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;

        public Task Held => _held.Task;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public void Release() => _released.TrySetResult();

        public async Task WaitIfArmedAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _held.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class HoldingHandler(HoldNextRequest hold) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await hold.WaitIfArmedAsync(cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (localhost:443)");
    }
}
