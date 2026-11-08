using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Panopticon.Server.Data;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the app or reaching a database.</summary>
internal sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<PanopticonDb>
{
    public PanopticonDb CreateDbContext(string[] args) =>
        new(DbOptions.Configure(new DbContextOptionsBuilder<PanopticonDb>(), "Host=localhost;Database=design").Options);
}

internal static class DbOptions
{
    public static DbContextOptionsBuilder<PanopticonDb> Configure(
        DbContextOptionsBuilder<PanopticonDb> builder,
        string connectionString) =>
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
