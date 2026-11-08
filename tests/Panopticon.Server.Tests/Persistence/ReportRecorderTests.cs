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
    public async Task An_older_report_committing_last_does_not_roll_the_change_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        var older = harness.Clock.GetUtcNow();

        // The newer report (more ticked) commits first; the older one, received earlier but slower, commits last.
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = harness.Clock.GetUtcNow();
        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, ThreeOfFour), ct);
        harness.Clock.Advance(older - newer);
        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);

        await using var db = harness.NewDb();
        var change = await db.Changes.SingleAsync(ct);
        Assert.Equal((3, 4), (change.Snapshot.OverallDone, change.Snapshot.OverallTotal));
        Assert.Equal(newer, change.LastReportedAt);
        Assert.Equal(newer, (await db.ChangeAgents.SingleAsync(ct)).LastReportedAt);
        Assert.Equal(2, await db.ReportEvents.CountAsync(ct));
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

    // --- change-documents-and-history: Document retention, Reports kept for later ---

    [Fact]
    public async Task Unchanged_design_is_stored_once_and_both_manifests_reference_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(WithDocuments("team-auth", TwoOfFour, ("design.md", "# Design\n")), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.RecordAsync(WithDocuments("team-auth", ThreeOfFour, ("design.md", "# Design\n")), ct);

        var designHash = S3DocumentStore.Hash("# Design\n");
        var stored = await harness.S3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = harness.Bucket, Prefix = "documents/sha256/" }, ct);
        Assert.Equal(3, stored.S3Objects.Count);
        Assert.Single(stored.S3Objects, o => o.Key == S3DocumentStore.KeyFor(designHash));

        await using var db = harness.NewDb();
        var designs = await db.ReportDocuments.Where(d => d.Path == "design.md").ToListAsync(ct);
        Assert.Equal(2, designs.Count);
        Assert.All(designs, d => Assert.Equal(designHash, d.Sha256));
    }

    [Fact]
    public async Task Each_report_keeps_a_manifest_naming_the_version_it_carried()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        string[] designs = ["# Design v1\n", "# Design v2\n", "# Design v3\n"];

        foreach (var design in designs)
        {
            await harness.RecordAsync(WithDocuments("team-auth", TwoOfFour, ("design.md", design), ("proposal.md", "# Why\n")), ct);
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        await using var db = harness.NewDb();
        var manifests = await db.ReportEvents
            .OrderBy(e => e.Id)
            .Select(e => db.ReportDocuments.Where(d => d.ReportEventId == e.Id).OrderBy(d => d.Path).Select(d => d.Path + "=" + d.Sha256).ToList())
            .ToListAsync(ct);
        Assert.Equal(3, manifests.Count);
        for (var i = 0; i < designs.Length; i++)
        {
            Assert.Equal(
                [$"design.md={S3DocumentStore.Hash(designs[i])}", $"proposal.md={S3DocumentStore.Hash("# Why\n")}", $"tasks.md={S3DocumentStore.Hash(TwoOfFour)}"],
                manifests[i]);
        }
    }

    [Fact]
    public async Task A_report_from_an_older_cli_keeps_only_its_tasks()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(new ReportInput(_panopticon, "walking-skeleton", _claudeOnVm1, null, TwoOfFour), ct);

        await using var db = harness.NewDb();
        var manifest = await db.ReportDocuments.SingleAsync(ct);
        Assert.Equal(("tasks.md", S3DocumentStore.Hash(TwoOfFour), (await db.ReportEvents.SingleAsync(ct)).Id), (manifest.Path, manifest.Sha256, manifest.ReportEventId));
        var latest = await db.ChangeDocuments.SingleAsync(ct);
        Assert.Equal(("tasks.md", TwoOfFour.Length), (latest.Path, latest.SizeBytes));
    }

    [Fact]
    public async Task A_newer_report_replaces_the_latest_documents_whole()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        await harness.RecordAsync(WithDocuments("team-auth", TwoOfFour, ("design.md", "# Design\n"), ("devlog/a.md", "a")), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.RecordAsync(WithDocuments("team-auth", ThreeOfFour, ("design.md", "# Design v2\n")), ct);

        await using var db = harness.NewDb();
        var latest = await db.ChangeDocuments.OrderBy(d => d.Path).ToListAsync(ct);
        Assert.Equal(["design.md", "tasks.md"], latest.Select(d => d.Path));
        Assert.Equal(S3DocumentStore.Hash("# Design v2\n"), latest[0].Sha256);
        Assert.All(latest, d => Assert.Equal(harness.Clock.GetUtcNow(), d.UpdatedAt));
    }

    [Fact]
    public async Task An_older_report_committing_last_does_not_replace_the_newer_documents()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        var older = harness.Clock.GetUtcNow();

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.RecordAsync(WithDocuments("team-auth", ThreeOfFour, ("design.md", "# Newer\n")), ct);
        harness.Clock.Advance(older - harness.Clock.GetUtcNow());
        await harness.RecordAsync(WithDocuments("team-auth", TwoOfFour, ("design.md", "# Older\n"), ("proposal.md", "# Why\n")), ct);

        await using var db = harness.NewDb();
        var latest = await db.ChangeDocuments.OrderBy(d => d.Path).ToListAsync(ct);
        Assert.Equal(["design.md", "tasks.md"], latest.Select(d => d.Path));
        Assert.Equal(S3DocumentStore.Hash("# Newer\n"), latest[0].Sha256);
        Assert.Equal(5, await db.ReportDocuments.CountAsync(ct));
    }

    // --- change-documents-and-history: Archived report, Change identity ---

    [Fact]
    public async Task An_archived_report_archives_the_change_with_its_final_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        var first = await harness.RecordAsync(WithDocuments("team-auth", TwoOfFour, ("design.md", "# Design\n")), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        var archived = await harness.RecordAsync(
            WithDocuments("team-auth", ThreeOfFour, ("design.md", "# Final\n")) with { Archive = "2026-11-08-team-auth" },
            ct);

        Assert.Equal((first.ChangeId, ArchiveOutcome.Archived), (archived.ChangeId, archived.Archive));
        await using var db = harness.NewDb();
        var change = await db.Changes.SingleAsync(ct);
        Assert.Equal((harness.Clock.GetUtcNow(), "2026-11-08-team-auth"), (change.ArchivedAt, change.ArchiveFolder));
        Assert.Equal(3, change.Snapshot.OverallDone);
        Assert.Contains(await db.ChangeDocuments.ToListAsync(ct), d => d.Path == "design.md" && d.Sha256 == S3DocumentStore.Hash("# Final\n"));
        Assert.Equal(2, await db.ReportEvents.CountAsync(ct));
    }

    [Fact]
    public async Task A_second_archived_report_changes_nothing_and_returns_the_final_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        var first = await harness.RecordAsync(
            WithDocuments("team-auth", ThreeOfFour, ("design.md", "# Final\n")) with { Archive = "2026-11-08-team-auth" },
            ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        // The agent reports after the watcher already did, and even with different content, nothing moves.
        var second = await harness.RecordAsync(
            WithDocuments("Team-Auth", TwoOfFour, ("design.md", "# Edited later\n")) with { Archive = "2026-11-08-team-auth" },
            ct);

        Assert.Equal((first.ChangeId, ArchiveOutcome.AlreadyArchived), (second.ChangeId, second.Archive));
        Assert.Equal((3, 4), (second.Snapshot.OverallDone, second.Snapshot.OverallTotal));
        await using var db = harness.NewDb();
        var change = await db.Changes.SingleAsync(ct);
        Assert.Equal(3, change.Snapshot.OverallDone);
        Assert.Equal(first.ReceivedAt, change.ArchivedAt);
        Assert.Equal(1, await db.ReportEvents.CountAsync(ct));
        Assert.Contains(await db.ChangeDocuments.ToListAsync(ct), d => d.Path == "design.md" && d.Sha256 == S3DocumentStore.Hash("# Final\n"));
    }

    [Fact]
    public async Task An_archived_report_for_an_unseen_change_records_it_archived()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);

        var recorded = await harness.RecordAsync(WithDocuments("walking-skeleton", ThreeOfFour) with { Archive = "2026-11-07-walking-skeleton" }, ct);

        Assert.Equal(ArchiveOutcome.Archived, recorded.Archive);
        await using var db = harness.NewDb();
        var change = await db.Changes.SingleAsync(ct);
        Assert.Equal(("walking-skeleton", "2026-11-07-walking-skeleton"), (change.Name, change.ArchiveFolder));
        Assert.NotNull(change.ArchivedAt);
    }

    [Fact]
    public async Task A_name_reused_after_an_archive_starts_a_new_active_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        var archived = await harness.RecordAsync(WithDocuments("cleanup", ThreeOfFour) with { Archive = "2026-03-01-cleanup" }, ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        var reused = await harness.RecordAsync(WithDocuments("cleanup", TwoOfFour), ct);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var again = await harness.RecordAsync(WithDocuments("cleanup", ThreeOfFour), ct);

        Assert.NotEqual(archived.ChangeId, reused.ChangeId);
        Assert.Equal(reused.ChangeId, again.ChangeId);
        await using var db = harness.NewDb();
        var old = await db.Changes.SingleAsync(c => c.Id == archived.ChangeId, ct);
        Assert.Equal(("2026-03-01-cleanup", 3), (old.ArchiveFolder, old.Snapshot.OverallDone));
        var active = await db.Changes.SingleAsync(c => c.ArchivedAt == null, ct);
        Assert.Equal(reused.ChangeId, active.Id);
    }

    [Fact]
    public async Task Concurrent_archived_reports_archive_the_change_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = await Harness.CreateAsync(postgres, s3, ct);
        await harness.RecordAsync(WithDocuments("team-auth", TwoOfFour), ct);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            harness.RecordAsync(WithDocuments("team-auth", ThreeOfFour) with { Archive = "2026-11-08-team-auth" }, ct)));

        Assert.Single(results, r => r.Archive == ArchiveOutcome.Archived);
        Assert.Single(results.Select(r => r.ChangeId).Distinct());
        await using var db = harness.NewDb();
        Assert.Equal(1, await db.Changes.CountAsync(ct));
    }

    private static ReportInput WithDocuments(string change, string tasks, params (string Path, string Content)[] others) =>
        new(_panopticon, change, _claudeOnVm1, null, tasks)
        {
            Documents = [new("tasks.md", tasks), .. others.Select(d => new Contracts.ChangeDocument(d.Path, d.Content))],
        };

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
