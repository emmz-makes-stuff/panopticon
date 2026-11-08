using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Server.Data;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Panopticon.Server.Tests.Infrastructure.PostgresFixture))]

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// One Postgres container for the whole test assembly. Each test asks for its own database, migrated
/// to the latest schema, so tests never share rows.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.11-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty, migrated database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var name = "test_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
        }.ConnectionString;

        await using var db = CreateDb(connectionString);
        await db.Database.MigrateAsync(cancellationToken);

        return connectionString;
    }

    public static PanopticonDb CreateDb(string connectionString) =>
        new(DbOptions.Configure(new DbContextOptionsBuilder<PanopticonDb>(), connectionString).Options);
}
