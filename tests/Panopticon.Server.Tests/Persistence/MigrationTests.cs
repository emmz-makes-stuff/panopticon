using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Persistence;

public class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Initial_migration_creates_the_design_D6_tables()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync(ct);
        await using var db = PostgresFixture.CreateDb(connectionString);

        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync(ct);

        Assert.Superset(
            new HashSet<string>(["repos", "changes", "agents", "change_agents", "report_events"]),
            tables.ToHashSet());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    [Fact]
    public async Task Team_auth_migration_creates_the_auth_tables()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));

        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync(ct);

        Assert.Superset(
            new HashSet<string>(["members", "sign_in_links", "sessions", "device_logins", "api_tokens"]),
            tables.ToHashSet());
    }

    [Fact]
    public async Task Walking_skeleton_data_survives_the_team_auth_migration_with_no_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync(InitialMigration, ct);
        await using (var before = PostgresFixture.CreateDb(connectionString))
        {
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO agents (id, client_name, machine_name, client_name_key, machine_name_key) VALUES ({Guid.NewGuid()}, 'claude-code', 'vm-1', 'claude-code', 'vm-1')",
                ct);
        }

        await using var db = PostgresFixture.CreateDb(connectionString);
        await db.Database.MigrateAsync(ct);

        var agent = await db.Agents.SingleAsync(ct);
        Assert.Equal(("claude-code", "vm-1"), (agent.ClientName, agent.MachineName));
        Assert.Null(agent.MemberId);
    }

    [Fact]
    public async Task Agents_without_a_member_are_still_unique_by_client_and_machine()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));

        db.Agents.Add(NewAgent(memberId: null));
        await db.SaveChangesAsync(ct);
        db.Agents.Add(NewAgent(memberId: null));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Two_members_can_each_have_the_same_client_on_the_same_machine()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var emmz = NewMember("emmz@example.com");
        var sam = NewMember("sam@example.com");
        db.Members.AddRange(emmz, sam);
        await db.SaveChangesAsync(ct);

        db.Agents.AddRange(NewAgent(emmz.Id), NewAgent(sam.Id));
        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.Agents.CountAsync(ct));
    }

    [Fact]
    public async Task Member_email_keys_are_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));

        db.Members.Add(NewMember("emmz@example.com"));
        await db.SaveChangesAsync(ct);
        db.Members.Add(NewMember("emmz@example.com"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Documents_migration_creates_the_document_tables()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));

        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync(ct);

        Assert.Superset(new HashSet<string>(["report_documents", "change_documents"]), tables.ToHashSet());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    [Fact]
    public async Task Team_auth_data_survives_the_documents_migration_as_active_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync(TeamAuthMigration, ct);
        var (repoId, changeId) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var before = PostgresFixture.CreateDb(connectionString))
        {
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO repos (id, host, owner, name, host_key, owner_key, name_key) VALUES ({repoId}, 'github.com', 'emmz', 'panopticon', 'github.com', 'emmz', 'panopticon')",
                ct);
            await before.Database.ExecuteSqlAsync(
                $$"""INSERT INTO changes (id, repo_id, name, name_key, last_reported_at, snapshot) VALUES ({{changeId}}, {{repoId}}, 'team-auth', 'team-auth', now(), '{"OverallDone":3,"OverallTotal":5,"CurrentSectionDone":0,"CurrentSectionTotal":0}')""",
                ct);
        }

        await using var db = PostgresFixture.CreateDb(connectionString);
        await db.Database.MigrateAsync(ct);

        var change = await db.Changes.SingleAsync(ct);
        Assert.Equal((changeId, "team-auth", 3), (change.Id, change.Name, change.Snapshot.OverallDone));
        Assert.Null(change.ArchivedAt);
        Assert.Null(change.ArchiveFolder);
    }

    [Fact]
    public async Task Two_active_changes_with_one_name_are_still_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var repo = NewRepo();
        db.Repos.Add(repo);
        db.Changes.Add(NewChange(repo.Id));
        await db.SaveChangesAsync(ct);

        db.Changes.Add(NewChange(repo.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task An_archived_change_and_an_active_one_can_share_a_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var repo = NewRepo();
        db.Repos.Add(repo);
        db.Changes.Add(NewChange(repo.Id, "2026-03-01-cleanup"));
        db.Changes.Add(NewChange(repo.Id, "2026-11-08-cleanup"));
        db.Changes.Add(NewChange(repo.Id));

        await db.SaveChangesAsync(ct);

        Assert.Equal(3, await db.Changes.CountAsync(ct));
    }

    [Fact]
    public async Task An_archive_folder_is_unique_per_repo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var repo = NewRepo();
        db.Repos.Add(repo);
        db.Changes.Add(NewChange(repo.Id, "2026-11-08-cleanup"));
        await db.SaveChangesAsync(ct);

        db.Changes.Add(NewChange(repo.Id, "2026-11-08-cleanup"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Older_report_events_survive_the_metrics_migration_with_no_progress_yet()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync(DocumentsMigration, ct);
        var (repoId, changeId, agentId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var before = PostgresFixture.CreateDb(connectionString))
        {
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO repos (id, host, owner, name, host_key, owner_key, name_key) VALUES ({repoId}, 'github.com', 'emmz', 'panopticon', 'github.com', 'emmz', 'panopticon')",
                ct);
            await before.Database.ExecuteSqlAsync(
                $$"""INSERT INTO changes (id, repo_id, name, name_key, last_reported_at, snapshot) VALUES ({{changeId}}, {{repoId}}, 'team-auth', 'team-auth', now(), '{"OverallDone":3,"OverallTotal":5,"CurrentSectionDone":0,"CurrentSectionTotal":0}')""",
                ct);
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO agents (id, client_name, machine_name, client_name_key, machine_name_key) VALUES ({agentId}, 'claude-code', 'vm-1', 'claude-code', 'vm-1')",
                ct);
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO report_events (change_id, agent_id, tasks_sha256, received_at) VALUES ({changeId}, {agentId}, {new string('a', 64)}, now())",
                ct);
        }

        await using var db = PostgresFixture.CreateDb(connectionString);
        await db.Database.MigrateAsync(ct);

        var reportEvent = await db.ReportEvents.SingleAsync(ct);
        Assert.Equal(changeId, reportEvent.ChangeId);
        Assert.Equal((null, null, null), (reportEvent.OverallDone, reportEvent.OverallTotal, reportEvent.CurrentSection));
        Assert.Null(reportEvent.Sections);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    // projects D1: the scope is part of a change's identity, with nulls equal.
    [Fact]
    public async Task Active_changes_differing_only_by_scope_coexist_and_the_same_scope_twice_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var (emmz, kit) = (NewMember("emmz@example.com"), NewMember("kit@example.com"));
        var project = NewProject("PAN", emmz.Id);
        var repo = NewRepo();
        db.AddRange(emmz, kit, project, repo);
        await db.SaveChangesAsync(ct);

        db.Changes.AddRange(
            NewChange(repo.Id, projectId: project.Id),
            NewChange(repo.Id, ownerMemberId: emmz.Id),
            NewChange(repo.Id, ownerMemberId: kit.Id),
            NewChange(repo.Id));
        await db.SaveChangesAsync(ct);
        Assert.Equal(4, await db.Changes.CountAsync(ct));

        foreach (var duplicate in new[]
        {
            NewChange(repo.Id, projectId: project.Id),
            NewChange(repo.Id, ownerMemberId: emmz.Id),
            NewChange(repo.Id),
        })
        {
            db.ChangeTracker.Clear();
            db.Changes.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        }
    }

    [Fact]
    public async Task A_change_cannot_be_in_a_project_and_personal_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var emmz = NewMember("emmz@example.com");
        var project = NewProject("PAN", emmz.Id);
        var repo = NewRepo();
        db.AddRange(emmz, project, repo);
        await db.SaveChangesAsync(ct);

        db.Changes.Add(NewChange(repo.Id, projectId: project.Id, ownerMemberId: emmz.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Project_codes_are_unique_and_roles_are_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var emmz = NewMember("emmz@example.com");
        var project = NewProject("PAN", emmz.Id);
        db.AddRange(emmz, project);
        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();
        db.Projects.Add(NewProject("PAN", emmz.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        db.ChangeTracker.Clear();
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, MemberId = emmz.Id, Role = "admin" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    // projects D1, Migration Plan: older changes become their first reporting member's personal work.
    [Fact]
    public async Task Older_changes_become_the_personal_work_of_their_first_reporting_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync(MetricsMigration, ct);
        var (repoId, memberChange, bootstrapChange) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (emmz, kit, bootstrapAgent, emmzAgent, kitAgent) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var before = PostgresFixture.CreateDb(connectionString))
        {
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO repos (id, host, owner, name, host_key, owner_key, name_key) VALUES ({repoId}, 'github.com', 'emmz', 'panopticon', 'github.com', 'emmz', 'panopticon')",
                ct);
            foreach (var (id, email) in new[] { (emmz, "emmz@example.com"), (kit, "kit@example.com") })
            {
                await before.Database.ExecuteSqlAsync(
                    $"INSERT INTO members (id, email, email_key, display_name, created_at) VALUES ({id}, {email}, {email}, {email}, now())",
                    ct);
            }

            foreach (var (id, member, machine) in new[] { (bootstrapAgent, (Guid?)null, "vm-0"), (emmzAgent, emmz, "vm-1"), (kitAgent, kit, "vm-2") })
            {
                await before.Database.ExecuteSqlAsync(
                    $"INSERT INTO agents (id, member_id, client_name, machine_name, client_name_key, machine_name_key) VALUES ({id}, {member}, 'claude-code', {machine}, 'claude-code', {machine})",
                    ct);
            }

            foreach (var (id, name) in new[] { (memberChange, "team-auth"), (bootstrapChange, "walking-skeleton") })
            {
                await before.Database.ExecuteSqlAsync(
                    $$"""INSERT INTO changes (id, repo_id, name, name_key, last_reported_at, snapshot) VALUES ({{id}}, {{repoId}}, {{name}}, {{name}}, now(), '{"OverallDone":0,"OverallTotal":1,"CurrentSectionDone":0,"CurrentSectionTotal":0}')""",
                    ct);
            }

            // team-auth: a bootstrap report first, then Emmz, then Kit. walking-skeleton: bootstrap reports only.
            foreach (var (change, agent, minutes) in new[] { (memberChange, bootstrapAgent, 1), (memberChange, emmzAgent, 2), (memberChange, kitAgent, 3), (bootstrapChange, bootstrapAgent, 1) })
            {
                var at = DateTimeOffset.UnixEpoch.AddMinutes(minutes);
                await before.Database.ExecuteSqlAsync(
                    $"INSERT INTO report_events (change_id, agent_id, tasks_sha256, received_at) VALUES ({change}, {agent}, {new string('a', 64)}, {at})",
                    ct);
            }
        }

        await using var db = PostgresFixture.CreateDb(connectionString);
        await db.Database.MigrateAsync(ct);

        var scopes = await db.Changes.ToDictionaryAsync(c => c.Id, c => (c.ProjectId, c.OwnerMemberId), ct);
        Assert.Equal((null, emmz), scopes[memberChange]);
        Assert.Equal((null, null), scopes[bootstrapChange]);
    }

    [Fact]
    public async Task The_projects_migration_rolls_back_keeping_one_change_per_old_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var emmz = NewMember("emmz@example.com");
        var project = NewProject("PAN", emmz.Id);
        var repo = NewRepo();
        db.AddRange(emmz, project, repo);
        await db.SaveChangesAsync(ct);
        var personal = NewChange(repo.Id, ownerMemberId: emmz.Id, lastReportedAt: DateTimeOffset.UnixEpoch);
        db.Changes.AddRange(personal, NewChange(repo.Id, projectId: project.Id, lastReportedAt: DateTimeOffset.UnixEpoch.AddDays(1)));
        await db.SaveChangesAsync(ct);

        await db.GetService<IMigrator>().MigrateAsync(MetricsMigration, cancellationToken: ct);

        var remaining = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM changes").ToListAsync(ct);
        Assert.Equal([personal.Id], remaining);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = 'projects'").SingleAsync(ct));
    }

    // tags D2: a tag is unique in its Project without regard to case, and two Projects may each have it.
    [Fact]
    public async Task Project_tags_are_unique_per_project_without_regard_to_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var emmz = NewMember("emmz@example.com");
        var (pan, acme) = (NewProject("PAN", emmz.Id), NewProject("ACME", emmz.Id));
        db.AddRange(emmz, pan, acme);
        db.ProjectTags.AddRange(NewTag(pan.Id, "area", "Onboarding"), NewTag(acme.Id, "area", "onboarding"), NewTag(pan.Id, "area"));
        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();
        db.ProjectTags.Add(NewTag(pan.Id, "Area", "ONBOARDING"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    // tags D2: deleting a tag, or a change, takes its change_project_tags rows with it; personal tags go with their agent row.
    [Fact]
    public async Task Deleting_a_tag_or_a_change_removes_what_hangs_off_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));
        var emmz = NewMember("emmz@example.com");
        var project = NewProject("PAN", emmz.Id);
        var repo = NewRepo();
        var agent = NewAgent(emmz.Id);
        db.AddRange(emmz, project, repo, agent);
        await db.SaveChangesAsync(ct);
        var (bug, onboarding) = (NewTag(project.Id, "bug"), NewTag(project.Id, "area", "onboarding"));
        var change = NewChange(repo.Id, projectId: project.Id);
        db.AddRange(bug, onboarding, change);
        db.ChangeProjectTags.AddRange(
            new ChangeProjectTag { ChangeId = change.Id, TagId = bug.Id },
            new ChangeProjectTag { ChangeId = change.Id, TagId = onboarding.Id });
        db.ChangeAgents.Add(new ChangeAgent { ChangeId = change.Id, AgentId = agent.Id, LastReportedAt = DateTimeOffset.UnixEpoch });
        db.ChangeAgentTags.Add(new ChangeAgentTag { ChangeId = change.Id, AgentId = agent.Id, Key = "dev", KeyKey = "dev", ValueKey = "" });
        await db.SaveChangesAsync(ct);

        await db.ProjectTags.Where(t => t.Id == bug.Id).ExecuteDeleteAsync(ct);
        Assert.Equal([onboarding.Id], await db.ChangeProjectTags.Select(t => t.TagId).ToListAsync(ct));

        await db.ChangeAgents.Where(a => a.ChangeId == change.Id).ExecuteDeleteAsync(ct);
        Assert.Equal(0, await db.ChangeAgentTags.CountAsync(ct));

        await db.Changes.Where(c => c.Id == change.Id).ExecuteDeleteAsync(ct);
        Assert.Equal(0, await db.ChangeProjectTags.CountAsync(ct));
        Assert.Equal(1, await db.ProjectTags.CountAsync(ct));
    }

    [Fact]
    public async Task The_tags_migration_rolls_back()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));

        await db.GetService<IMigrator>().MigrateAsync(RetiredCopiesMigration, cancellationToken: ct);

        Assert.Equal(0, await db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name IN ('project_tags', 'change_project_tags', 'change_agent_tags')").SingleAsync(ct));
    }

    private static ProjectTag NewTag(Guid projectId, string key, string? value = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Key = key,
        Value = value,
        KeyKey = key.ToLowerInvariant(),
        ValueKey = value?.ToLowerInvariant() ?? string.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private const string RetiredCopiesMigration = "20261110011205_RetiredCopies";
    private const string MetricsMigration = "20261109164834_ChangeMetrics";
    private const string DocumentsMigration = "20261108184053_ChangeDocumentsAndHistory";
    private const string InitialMigration = "20261108001226_Initial";
    private const string TeamAuthMigration = "20261108150815_TeamAuth";

    private static Change NewChange(
        Guid repoId,
        string? archiveFolder = null,
        Guid? projectId = null,
        Guid? ownerMemberId = null,
        DateTimeOffset lastReportedAt = default) => new()
        {
            Id = Guid.NewGuid(),
            RepoId = repoId,
            Name = "cleanup",
            NameKey = "cleanup",
            Snapshot = new ProgressSnapshot(),
            ArchivedAt = archiveFolder is null ? null : DateTimeOffset.UnixEpoch,
            ArchiveFolder = archiveFolder,
            ProjectId = projectId,
            OwnerMemberId = ownerMemberId,
            LastReportedAt = lastReportedAt,
        };

    private static Project NewProject(string code, Guid createdBy) => new()
    {
        Id = Guid.NewGuid(),
        Name = code,
        Code = code,
        CodeKey = code.ToLowerInvariant(),
        CreatedAt = DateTimeOffset.UnixEpoch,
        CreatedByMemberId = createdBy,
    };

    private static Member NewMember(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        EmailKey = email,
        DisplayName = email.Split('@')[0],
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static Agent NewAgent(Guid? memberId) => new()
    {
        MemberId = memberId,
        ClientName = "claude-code",
        MachineName = "vm-1",
        ClientNameKey = "claude-code",
        MachineNameKey = "vm-1",
    };

    [Fact]
    public async Task Repo_identity_keys_are_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = PostgresFixture.CreateDb(await postgres.CreateDatabaseAsync(ct));

        db.Repos.Add(NewRepo());
        await db.SaveChangesAsync(ct);
        db.Repos.Add(NewRepo());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    private static Repo NewRepo() => new()
    {
        Host = "github.com",
        Owner = "emmz",
        Name = "panopticon",
        HostKey = "github.com",
        OwnerKey = "emmz",
        NameKey = "panopticon",
    };
}
