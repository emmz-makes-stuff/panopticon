using Microsoft.EntityFrameworkCore;

namespace Panopticon.Server.Data;

public sealed class PanopticonDb(DbContextOptions<PanopticonDb> options) : DbContext(options)
{
    public DbSet<Repo> Repos => Set<Repo>();
    public DbSet<Change> Changes => Set<Change>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<ChangeAgent> ChangeAgents => Set<ChangeAgent>();
    public DbSet<ReportEvent> ReportEvents => Set<ReportEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Repo>(repo =>
        {
            repo.HasIndex(r => new { r.HostKey, r.OwnerKey, r.NameKey }).IsUnique();
            repo.HasMany(r => r.Changes).WithOne(c => c.Repo).HasForeignKey(c => c.RepoId);
        });

        modelBuilder.Entity<Change>(change =>
        {
            change.HasIndex(c => new { c.RepoId, c.NameKey }).IsUnique();
            change.HasIndex(c => c.LastReportedAt);
            change.OwnsOne(c => c.Snapshot, snapshot => snapshot.ToJson());
            change.HasMany(c => c.Agents).WithOne().HasForeignKey(a => a.ChangeId);
        });

        modelBuilder.Entity<Agent>(agent =>
            agent.HasIndex(a => new { a.ClientNameKey, a.MachineNameKey }).IsUnique());

        modelBuilder.Entity<ChangeAgent>(changeAgent =>
        {
            changeAgent.HasKey(ca => new { ca.ChangeId, ca.AgentId });
            changeAgent.HasOne(ca => ca.Agent).WithMany().HasForeignKey(ca => ca.AgentId);
        });

        modelBuilder.Entity<ReportEvent>(reportEvent =>
        {
            reportEvent.Property(e => e.TasksSha256).HasMaxLength(64).IsFixedLength();
            reportEvent.HasIndex(e => new { e.ChangeId, e.ReceivedAt });
            reportEvent.HasOne<Change>().WithMany().HasForeignKey(e => e.ChangeId);
            reportEvent.HasOne<Agent>().WithMany().HasForeignKey(e => e.AgentId);
        });
    }
}
