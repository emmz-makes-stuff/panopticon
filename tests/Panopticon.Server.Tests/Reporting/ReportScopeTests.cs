using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Reporting;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Reporting;

// projects D4; spec progress-reporting: Reporting into a Project, Change identity (Two scopes, two changes; Two people,
// no Project), Report from a CLI without Projects; spec change-visibility: A change's scope, Development bootstrap reports.
public class ReportScopeTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1\n- [ ] 1.2\n";

    private static readonly RepoIdentity _panopticon = new("github.com", "emmz", "panopticon");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_reporting_into_their_Project_puts_the_change_in_its_scope()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (emmz, pan) = (await harness.MemberAsync("emmz"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, emmz, ProjectRoles.Owner);

        var recorded = await harness.RecordAsync(emmz, pan);

        Assert.Equal((pan, (Guid?)null), await harness.ScopeAsync(recorded.ChangeId));
    }

    [Fact]
    public async Task A_report_naming_no_Project_is_the_reporters_own_and_a_bootstrap_one_is_nobodys()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var emmz = await harness.MemberAsync("emmz");

        var personal = await harness.RecordAsync(emmz, project: null);
        var bootstrap = await harness.RecordAsync(member: null, project: null, change: "walking-skeleton");

        Assert.Equal(((Guid?)null, (Guid?)emmz), await harness.ScopeAsync(personal.ChangeId));
        Assert.Equal(((Guid?)null, (Guid?)null), await harness.ScopeAsync(bootstrap.ChangeId));
    }

    [Fact]
    public async Task A_viewer_reports_into_the_Project()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (kit, pan) = (await harness.MemberAsync("kit"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, kit, ProjectRoles.Viewer);

        var recorded = await harness.RecordAsync(kit, pan);

        Assert.Equal((pan, (Guid?)null), await harness.ScopeAsync(recorded.ChangeId));
    }

    [Fact]
    public async Task A_report_into_a_Project_the_reporter_isnt_in_or_that_doesnt_exist_is_refused_and_records_nothing()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (sam, acme) = (await harness.MemberAsync("sam"), await harness.ProjectAsync("ACME"));
        var unknown = Guid.NewGuid();

        var notMember = await Assert.ThrowsAsync<ReportRefusedException>(() => harness.RecordAsync(sam, acme));
        var missing = await Assert.ThrowsAsync<ReportRefusedException>(() => harness.RecordAsync(sam, unknown));
        var bootstrapMissing = await Assert.ThrowsAsync<ReportRefusedException>(() => harness.RecordAsync(member: null, unknown));

        Assert.Contains(acme.ToString(), notMember.Message, StringComparison.Ordinal);
        Assert.Contains("list_projects", notMember.Message, StringComparison.Ordinal);
        Assert.Contains(unknown.ToString(), missing.Message, StringComparison.Ordinal);
        Assert.NotNull(bootstrapMissing);
        await using var db = harness.NewDb();
        Assert.Equal((0, 0), (await db.Changes.CountAsync(Ct), await db.ReportEvents.CountAsync(Ct)));
    }

    [Fact]
    public async Task A_bootstrap_report_may_name_any_existing_Project()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var pan = await harness.ProjectAsync("PAN");

        var recorded = await harness.RecordAsync(member: null, pan);

        Assert.Equal((pan, (Guid?)null), await harness.ScopeAsync(recorded.ChangeId));
    }

    [Fact]
    public async Task Two_scopes_are_two_changes_and_two_people_with_no_Project_each_have_their_own()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (emmz, kit, pan) = (await harness.MemberAsync("emmz"), await harness.MemberAsync("kit"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, emmz, ProjectRoles.Owner);

        var inPan = await harness.RecordAsync(emmz, pan, change: "cleanup");
        var kits = await harness.RecordAsync(kit, project: null, change: "cleanup");
        var emmzScratch = await harness.RecordAsync(emmz, project: null, change: "scratch");
        var kitScratch = await harness.RecordAsync(kit, project: null, change: "scratch");
        var kitAgain = await harness.RecordAsync(kit, project: null, change: "scratch");

        Assert.Equal(4, new[] { inPan, kits, emmzScratch, kitScratch }.Select(r => r.ChangeId).Distinct().Count());
        Assert.Equal(kitScratch.ChangeId, kitAgain.ChangeId);
        Assert.Equal(((Guid?)null, (Guid?)kit), await harness.ScopeAsync(kits.ChangeId));
    }

    // --- 3.2: spec change-visibility, "Linking moves personal work" ---

    [Fact]
    public async Task Linking_mid_change_moves_the_personal_change_with_all_its_events_into_the_Project()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (emmz, pan) = (await harness.MemberAsync("emmz"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, emmz, ProjectRoles.Owner);

        var first = await harness.RecordAsync(emmz, project: null);
        await harness.RecordAsync(emmz, project: null);
        var linked = await harness.RecordAsync(emmz, pan);

        Assert.Equal(first.ChangeId, linked.ChangeId);
        Assert.Null(linked.RetiredChangeId);
        Assert.Equal((pan, (Guid?)null), await harness.ScopeAsync(linked.ChangeId));
        await using var db = harness.NewDb();
        Assert.Equal(1, await db.Changes.CountAsync(Ct));
        var events = await db.ReportEvents.Where(e => e.ChangeId == linked.ChangeId).Select(e => e.ReceivedAt).ToListAsync(Ct);
        Assert.Equal(3, events.Count);

        // Time to archive is counted from the earliest event, which is still the first, unlinked report.
        Assert.Equal(first.ReceivedAt, events.Min());
    }

    [Fact]
    public async Task Linking_leaves_a_teammates_personal_change_of_the_same_name_untouched()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (emmz, kit, pan) = (await harness.MemberAsync("emmz"), await harness.MemberAsync("kit"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, emmz, ProjectRoles.Owner);
        await harness.JoinAsync(pan, kit, ProjectRoles.Viewer);

        var kits = await harness.RecordAsync(kit, project: null);
        var linked = await harness.RecordAsync(emmz, pan);

        Assert.NotEqual(kits.ChangeId, linked.ChangeId);
        Assert.Null(linked.RetiredChangeId);
        Assert.Equal(((Guid?)null, (Guid?)kit), await harness.ScopeAsync(kits.ChangeId));
        Assert.Null((await harness.ChangeAsync(kits.ChangeId)).ArchivedAt);
    }

    [Fact]
    public async Task An_archived_personal_change_isnt_moved_and_the_linked_report_starts_a_new_one()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (emmz, pan) = (await harness.MemberAsync("emmz"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, emmz, ProjectRoles.Owner);
        var personal = await harness.RecordAsync(emmz, project: null);
        await harness.ArchiveAsync(personal.ChangeId, "2026-01-01-cleanup");

        var linked = await harness.RecordAsync(emmz, pan);

        Assert.NotEqual(personal.ChangeId, linked.ChangeId);
        Assert.Equal((pan, (Guid?)null), await harness.ScopeAsync(linked.ChangeId));
        Assert.Equal(((Guid?)null, (Guid?)emmz), await harness.ScopeAsync(personal.ChangeId));
        Assert.Equal("2026-01-01-cleanup", (await harness.ChangeAsync(personal.ChangeId)).ArchiveFolder);
    }

    // --- 3.2: spec change-visibility, "Linking when the Project already has the change" ---

    [Fact]
    public async Task Linking_when_the_Project_already_has_the_change_updates_it_and_retires_the_personal_one()
    {
        var harness = await Harness.CreateAsync(postgres, s3);
        var (emmz, kit, pan) = (await harness.MemberAsync("emmz"), await harness.MemberAsync("kit"), await harness.ProjectAsync("PAN"));
        await harness.JoinAsync(pan, emmz, ProjectRoles.Owner);
        await harness.JoinAsync(pan, kit, ProjectRoles.Viewer);
        var kits = await harness.RecordAsync(kit, pan);
        var personal = await harness.RecordAsync(emmz, project: null);

        var linked = await harness.RecordAsync(emmz, pan);

        Assert.Equal(kits.ChangeId, linked.ChangeId);
        Assert.Equal(personal.ChangeId, linked.RetiredChangeId);
        var retired = await harness.ChangeAsync(personal.ChangeId);
        Assert.Equal((linked.ReceivedAt, (string?)null), (retired.ArchivedAt, retired.ArchiveFolder));
        Assert.Equal(((Guid?)null, (Guid?)emmz), (retired.ProjectId, retired.OwnerMemberId));

        // Her next linked report finds only the Project's change: nothing more to retire.
        var again = await harness.RecordAsync(emmz, pan);
        Assert.Equal((kits.ChangeId, (Guid?)null), (again.ChangeId, again.RetiredChangeId));
    }

    private sealed class Harness
    {
        private readonly ManualClock _clock = ManualClock.StartingAt2026();

        public required string ConnectionString { get; init; }

        public required S3DocumentStore Documents { get; init; }

        public static async Task<Harness> CreateAsync(PostgresFixture postgres, S3Fixture s3)
        {
            var options = await s3.CreateBucketAsync(Ct);
            return new Harness
            {
                ConnectionString = await postgres.CreateDatabaseAsync(Ct),
                Documents = new S3DocumentStore(options.CreateClient(), options.Bucket!),
            };
        }

        public PanopticonDb NewDb() => PostgresFixture.CreateDb(ConnectionString);

        public async Task<Guid> MemberAsync(string name)
        {
            await using var db = NewDb();
            var member = new Member { Email = $"{name}@example.com", EmailKey = $"{name}@example.com", DisplayName = name, CreatedAt = _clock.GetUtcNow() };
            db.Members.Add(member);
            await db.SaveChangesAsync(Ct);
            return member.Id;
        }

        public async Task<Guid> ProjectAsync(string code)
        {
            await using var db = NewDb();
            var creator = new Member { Email = $"{code}-owner@example.com", EmailKey = $"{code.ToLowerInvariant()}-owner@example.com", DisplayName = code, CreatedAt = _clock.GetUtcNow() };
            var project = new Project { Id = Guid.NewGuid(), Name = code, Code = code, CodeKey = code.ToLowerInvariant(), CreatedAt = _clock.GetUtcNow() };
            db.Members.Add(creator);
            await db.SaveChangesAsync(Ct);
            project.CreatedByMemberId = creator.Id;
            db.Projects.Add(project);
            await db.SaveChangesAsync(Ct);
            return project.Id;
        }

        public async Task JoinAsync(Guid project, Guid member, string role)
        {
            await using var db = NewDb();
            db.ProjectMembers.Add(new ProjectMember { ProjectId = project, MemberId = member, Role = role, JoinedAt = _clock.GetUtcNow() });
            await db.SaveChangesAsync(Ct);
        }

        /// <summary>Records a report a minute after the last, by the member (null: the bootstrap token) into the Project.</summary>
        public async Task<RecordedReport> RecordAsync(Guid? member, Guid? project, string change = "cleanup")
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await using var db = NewDb();
            return await new ReportRecorder(db, Documents, _clock).RecordAsync(
                new ReportInput(_panopticon, change, new AgentIdentity("claude-code", "vm-1", member), null, Tasks) { ProjectId = project },
                Ct);
        }

        public async Task<(Guid? ProjectId, Guid? OwnerMemberId)> ScopeAsync(Guid changeId)
        {
            var change = await ChangeAsync(changeId);
            return (change.ProjectId, change.OwnerMemberId);
        }

        public async Task<Change> ChangeAsync(Guid changeId)
        {
            await using var db = NewDb();
            return await db.Changes.AsNoTracking().SingleAsync(c => c.Id == changeId, Ct);
        }

        public async Task ArchiveAsync(Guid changeId, string folder)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            var archivedAt = _clock.GetUtcNow();
            await using var db = NewDb();
            await db.Changes.Where(c => c.Id == changeId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ArchivedAt, archivedAt).SetProperty(c => c.ArchiveFolder, folder), Ct);
        }
    }
}
