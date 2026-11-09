using System.Net;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Progress;
using Panopticon.Server.Reporting;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Progress;

// change-metrics D3; spec: Per-report progress is kept (Older reports filled in, A missing tasks.md).
public class ProgressBackfillTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string TwoOfFour = """
        ## 1. Setup
        - [x] 1.1
        - [x] 1.2
        ## 2. Core
        - [ ] 2.1
        - [ ] 2.2
        ## Workflow follow-up
        - [ ] archive
        """;

    private const string FourOfFour = """
        ## 1. Setup
        - [x] 1.1
        - [x] 1.2
        ## 2. Core
        - [x] 2.1
        - [x] 2.2
        """;

    private static readonly string _missingHash = new('f', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Older_reports_are_filled_in_as_the_recorder_would_have()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);
        await harness.RecordAsync(FourOfFour, reportedSection: null);
        var recorded = await harness.ProgressAsync();
        await harness.ForgetProgressAsync();

        var result = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(Filled: 2, Unreadable: 0, Stopped: false), result);
        Assert.Equal(recorded, await harness.ProgressAsync());
        Assert.Collection(
            recorded,
            e => Assert.Equal("2/5 current 2 [1 Setup 2/2, 2 Core 0/2]", e),
            e => Assert.Equal("4/4 current 2 [1 Setup 2/2, 2 Core 2/2]", e));
    }

    [Fact]
    public async Task A_report_with_no_stored_tasks_is_marked_unreadable_and_logged_and_the_rest_are_filled()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);
        var missingId = await harness.RecordAsync(FourOfFour, reportedSection: null);
        await harness.RecordAsync(FourOfFour, reportedSection: null);
        await harness.ForgetProgressAsync();
        await using (var db = harness.NewDb())
        {
            await db.ReportEvents.Where(e => e.Id == missingId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.TasksSha256, _missingHash), Ct);
        }

        var result = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(Filled: 2, Unreadable: 1, Stopped: false), result);
        Assert.Equal(["2/5 current 2 [1 Setup 2/2, 2 Core 0/2]", "unreadable", "4/4 current 2 [1 Setup 2/2, 2 Core 2/2]"], await harness.ProgressAsync());
        var warning = Assert.Single(harness.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains($"report event {missingId} ", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_run_touches_nothing()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);
        var missingId = await harness.RecordAsync(FourOfFour, reportedSection: null);
        await harness.ForgetProgressAsync();
        await using (var db = harness.NewDb())
        {
            await db.ReportEvents.Where(e => e.Id == missingId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.TasksSha256, _missingHash), Ct);
        }

        await harness.Backfill().RunAsync(Ct);
        var afterFirst = await harness.ProgressAsync();
        var second = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(Filled: 0, Unreadable: 0, Stopped: false), second);
        Assert.Equal(afterFirst, await harness.ProgressAsync());
        Assert.Equal(0, harness.Store.Gets);
    }

    [Fact]
    public async Task Reports_already_kept_by_the_recorder_are_left_alone()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);

        var result = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(0, 0, false), result);
        Assert.Equal(0, harness.Store.Gets);
    }

    [Fact]
    public async Task A_store_error_stops_the_run_keeping_what_was_filled_and_the_next_run_finishes()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);
        await harness.RecordAsync(FourOfFour, reportedSection: null);
        await harness.ForgetProgressAsync();
        harness.FailOn = S3DocumentStore.Hash(FourOfFour);

        var stopped = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(Filled: 1, Unreadable: 0, Stopped: true), stopped);
        Assert.Equal(["2/5 current 2 [1 Setup 2/2, 2 Core 0/2]", "pending"], await harness.ProgressAsync());
        Assert.Contains(harness.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("stopped", StringComparison.Ordinal));

        harness.FailOn = null;
        var next = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(Filled: 1, Unreadable: 0, Stopped: false), next);
        Assert.Equal(["2/5 current 2 [1 Setup 2/2, 2 Core 0/2]", "4/4 current 2 [1 Setup 2/2, 2 Core 2/2]"], await harness.ProgressAsync());
    }

    [Fact]
    public async Task More_reports_than_a_batch_are_all_filled_fetching_each_tasks_once_per_batch()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);
        await using (var db = harness.NewDb())
        {
            // Copies of the one report, as identical reports from a busy change would be.
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO report_events (change_id, agent_id, reported_section, tasks_sha256, received_at) SELECT change_id, agent_id, reported_section, tasks_sha256, received_at FROM report_events, generate_series(1, {ProgressBackfill.BatchSize + 49})",
                Ct);
        }

        await harness.ForgetProgressAsync();

        var result = await harness.Backfill().RunAsync(Ct);

        Assert.Equal(new BackfillResult(Filled: ProgressBackfill.BatchSize + 50, Unreadable: 0, Stopped: false), result);
        Assert.All(await harness.ProgressAsync(), e => Assert.Equal("2/5 current 2 [1 Setup 2/2, 2 Core 0/2]", e));
        Assert.Equal(2, harness.Store.Gets);
    }

    [Fact]
    public async Task The_server_fills_older_reports_in_when_it_starts()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        await harness.RecordAsync(TwoOfFour, reportedSection: 2);
        await harness.ForgetProgressAsync();

        await using var factory = new PanopticonFactory(harness.ConnectionString, harness.S3Options);
        factory.CreateClient().Dispose();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while ((await harness.ProgressAsync()).Contains("pending") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        Assert.Equal(["2/5 current 2 [1 Setup 2/2, 2 Core 0/2]"], await harness.ProgressAsync());
    }

    private sealed class Harness
    {
        private readonly ManualClock _clock = ManualClock.StartingAt2026();

        public required string ConnectionString { get; init; }

        public required S3Options S3Options { get; init; }

        public required S3DocumentStore Documents { get; init; }

        /// <summary>A hash the next backfill's store fails to read.</summary>
        public string? FailOn { get; set; }

        public FlakyStore Store { get; private set; } = null!;

        public ListLogger<ProgressBackfill> Logger { get; private set; } = null!;

        public static async Task<Harness> CreateAsync(PostgresFixture postgres, S3Fixture s3)
        {
            var options = await s3.CreateBucketAsync(Ct);
            return new Harness
            {
                ConnectionString = await postgres.CreateDatabaseAsync(Ct),
                S3Options = options,
                Documents = new S3DocumentStore(options.CreateClient(), options.Bucket!),
            };
        }

        public PanopticonDb NewDb() => PostgresFixture.CreateDb(ConnectionString);

        /// <summary>Records a report a minute after the last, and returns its event's id.</summary>
        public async Task<long> RecordAsync(string tasks, int? reportedSection)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await using var db = NewDb();
            await new ReportRecorder(db, Documents, _clock).RecordAsync(
                new ReportInput(new("github.com", "emmz", "panopticon"), "change-metrics", new("claude-code", "vm-1"), reportedSection, tasks),
                Ct);
            return await db.ReportEvents.MaxAsync(e => e.Id, Ct);
        }

        /// <summary>Clears every event's progress, as for reports accepted before it was kept.</summary>
        public async Task ForgetProgressAsync()
        {
            await using var db = NewDb();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE report_events SET overall_done = NULL, overall_total = NULL, current_section = NULL, sections = NULL",
                Ct);
        }

        /// <summary>Each event's progress as text, in id order.</summary>
        public async Task<List<string>> ProgressAsync()
        {
            await using var db = NewDb();
            var events = await db.ReportEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
            return [.. events.Select(Describe)];
        }

        /// <summary>A fresh backfill over a store that counts its reads and can be made to fail.</summary>
        public ProgressBackfill Backfill()
        {
            Store = new FlakyStore(Documents) { FailOn = FailOn };
            Logger = new ListLogger<ProgressBackfill>();
            var services = new ServiceCollection()
                .AddScoped(_ => NewDb())
                .AddSingleton<IDocumentStore>(Store)
                .BuildServiceProvider();
            return new ProgressBackfill(services.GetRequiredService<IServiceScopeFactory>(), Logger);
        }

        private static string Describe(ReportEvent e) => e switch
        {
            { OverallTotal: null } => "pending",
            { OverallTotal: ReportEvent.Unreadable } => "unreadable",
            _ => $"{e.OverallDone}/{e.OverallTotal} current {e.CurrentSection} [{string.Join(", ", e.Sections!.Select(s => $"{s.Number} {s.Title} {s.Done}/{s.Total}"))}]",
        };
    }

    private sealed class FlakyStore(IDocumentStore inner) : IDocumentStore
    {
        private int _gets;

        public int Gets => _gets;

        /// <summary>A hash whose read fails as a transient store error would.</summary>
        public string? FailOn { get; set; }

        public Task<string> PutAsync(string content, CancellationToken cancellationToken) => inner.PutAsync(content, cancellationToken);

        public Task<string?> GetAsync(string sha256, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _gets);
            return sha256 == FailOn
                ? throw new AmazonS3Exception("Service unavailable") { StatusCode = HttpStatusCode.ServiceUnavailable }
                : inner.GetAsync(sha256, cancellationToken);
        }
    }
}
