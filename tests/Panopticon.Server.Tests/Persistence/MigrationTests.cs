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
