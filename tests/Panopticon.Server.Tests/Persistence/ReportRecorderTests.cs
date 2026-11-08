using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Progress;
using Panopticon.Server.Reporting;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Persistence;

// Scenarios from specs/progress-reporting: Change identity, Agents on a change, Document retention.
public class ReportRecorderTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string TwoOfFour = """
        ## 1. Setup
        - [x] 1.1
        - [x] 1.2
        ## 2. Core
        - [ ] 2.1
        - [ ] 2.2
        """;

    private const string ThreeOfFour = """
        ## 1. Setup
        - [x] 1.1
        - [x] 1.2
        ## 2. Core
        - [x] 2.1
        - [ ] 2.2
        """;

    private static readonly RepoIdentity _panopticon = new("github.com", "emmz", "panopticon");
    private static readonly AgentIdentity _claudeOnVm1 = new("claude-code", "vm-1");

    [Fact]
    public async Task Repeat_report_updates_the_same_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, ThreeOfFour), ct);

        await using var db = harness.NewDb();
        var change = await db.Changes.SingleAsync(ct);
        Assert.Equal(second.ChangeId, change.Id);
        Assert.Equal((3, 4), (change.Snapshot.OverallDone, change.Snapshot.OverallTotal));
        Assert.Equal((2, "Core", 1, 2), (change.Snapshot.CurrentSectionNumber, change.Snapshot.CurrentSectionTitle,
            change.Snapshot.CurrentSectionDone, change.Snapshot.CurrentSectionTotal));
        Assert.Equal(harness.Clock.GetUtcNow(), change.LastReportedAt);
    }

    [Fact]
    public async Task Identity_is_case_insensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(new ReportInput(new("GitHub.com", "Emmz", "Panopticon"), "Walking-Skeleton", _claudeOnVm1, null, TwoOfFour), ct);
        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", new("Claude-Code", "VM-1"), null, ThreeOfFour), ct);

        await using var db = harness.NewDb();
        Assert.Equal(1, await db.Repos.CountAsync(ct));
        Assert.Equal(1, await db.Changes.CountAsync(ct));
        Assert.Equal(1, await db.Agents.CountAsync(ct));
        // Display values keep the casing first reported.
        var repo = await db.Repos.SingleAsync(ct);
        Assert.Equal(("GitHub.com", "Emmz", "Panopticon"), (repo.Host, repo.Owner, repo.Name));
    }

    [Fact]
    public async Task Two_agents_on_one_change_each_keep_their_last_report_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);
        var claudeTime = harness.Clock.GetUtcNow();
        harness.Clock.Advance(TimeSpan.FromMinutes(3));
        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", new("cursor", "vm-2"), null, TwoOfFour), ct);

        await using var db = harness.NewDb();
        var agents = await db.ChangeAgents.Include(ca => ca.Agent)
            .OrderBy(ca => ca.LastReportedAt)
            .Select(ca => new { ca.Agent.ClientName, ca.Agent.MachineName, ca.LastReportedAt })
            .ToListAsync(ct);
        Assert.Collection(
            agents,
            a => Assert.Equal(("claude-code", "vm-1", claudeTime), (a.ClientName, a.MachineName, a.LastReportedAt)),
            a => Assert.Equal(("cursor", "vm-2", harness.Clock.GetUtcNow()), (a.ClientName, a.MachineName, a.LastReportedAt)));
    }

    [Fact]
    public async Task Report_is_kept_as_an_event_referencing_the_stored_document()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        var recorded = await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, 2, TwoOfFour), ct);

        await using var db = harness.NewDb();
        var reportEvent = await db.ReportEvents.SingleAsync(ct);
        Assert.Equal(recorded.ChangeId, reportEvent.ChangeId);
        var agent = await db.Agents.SingleAsync(a => a.Id == reportEvent.AgentId, ct);
        Assert.Equal(("claude-code", "vm-1"), (agent.ClientName, agent.MachineName));
        Assert.Equal(2, reportEvent.ReportedSection);
        Assert.Equal(recorded.ReceivedAt, reportEvent.ReceivedAt);
        Assert.Equal(TwoOfFour, await harness.Documents.GetAsync(reportEvent.TasksSha256, ct));
    }

    [Fact]
    public async Task Each_report_is_an_event_from_its_own_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", new("cursor", "vm-2"), null, TwoOfFour), ct);

        await using var db = harness.NewDb();
        var reporters = await db.ReportEvents
            .OrderBy(e => e.ReceivedAt)
            .Join(db.Agents, e => e.AgentId, a => a.Id, (e, a) => a.ClientName)
            .ToListAsync(ct);
        Assert.Equal(["claude-code", "cursor"], reporters);
    }

    // specs/progress-reporting "Unchanged document stored once", through the recorder rather than the store alone.
    [Fact]
    public async Task Identical_reports_are_two_events_sharing_one_stored_document()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);

        await using var db = harness.NewDb();
        var shas = await db.ReportEvents.Select(e => e.TasksSha256).ToListAsync(ct);
        Assert.Equal(2, shas.Count);
        Assert.Single(shas.Distinct());
        var listing = await harness.S3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = harness.Bucket }, ct);
        Assert.Single(listing.S3Objects);
    }

    [Fact]
    public async Task Oversized_tasks_records_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        var oversized = new string('a', TasksParser.MaxBytes + 1);

        await Assert.ThrowsAsync<TasksDocumentTooLargeException>(() =>
            harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, oversized), ct));

        await using var db = harness.NewDb();
        Assert.Equal(0, await db.Changes.CountAsync(ct));
        Assert.Equal(0, await db.ReportEvents.CountAsync(ct));
    }

    [Fact]
    public async Task Concurrent_first_reports_create_one_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct)));

        await using var db = harness.NewDb();
        Assert.Equal(1, await db.Changes.CountAsync(ct));
        Assert.Equal(4, await db.ReportEvents.CountAsync(ct));
    }

    private sealed class Harness
    {
        private string _connectionString = null!;

        public ManualClock Clock { get; } = ManualClock.StartingAt2026();
        public S3DocumentStore Documents { get; private set; } = null!;

        public IAmazonS3 S3 { get; private set; } = null!;

        public string Bucket { get; private set; } = null!;

        public static async Task<Harness> CreateAsync(PostgresFixture postgres, S3Fixture s3, CancellationToken ct)
        {
            var options = await s3.CreateBucketAsync(ct);
            var client = options.CreateClient();
            return new Harness
            {
                _connectionString = await postgres.CreateDatabaseAsync(ct),
                Documents = new S3DocumentStore(client, options.Bucket!),
                S3 = client,
                Bucket = options.Bucket!,
            };
        }

        public PanopticonDb NewDb() => PostgresFixture.CreateDb(_connectionString);

        /// <summary>Records through a fresh context, as each request would.</summary>
        public async Task<RecordedReport> RecordAsync(ReportInput report, CancellationToken ct)
        {
            await using var db = NewDb();
            return await new ReportRecorder(db, Documents, Clock).RecordAsync(report, ct);
        }
    }
}
