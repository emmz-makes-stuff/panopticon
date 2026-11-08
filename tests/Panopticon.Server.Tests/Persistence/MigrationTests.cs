using Microsoft.EntityFrameworkCore;
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

    private const string InitialMigration = "20261108001226_Initial";

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
