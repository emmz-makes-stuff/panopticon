using Microsoft.EntityFrameworkCore;

namespace Panopticon.Server.Data;

public sealed class PanopticonDb(DbContextOptions<PanopticonDb> options) : DbContext(options)
{
    public DbSet<Repo> Repos => Set<Repo>();
    public DbSet<Change> Changes => Set<Change>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<ChangeAgent> ChangeAgents => Set<ChangeAgent>();
    public DbSet<ReportEvent> ReportEvents => Set<ReportEvent>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<SignInLink> SignInLinks => Set<SignInLink>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<DeviceLogin> DeviceLogins => Set<DeviceLogin>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

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

        // team-auth D2: an agent is a member's client on a machine; agents with no member still count as one group.
        modelBuilder.Entity<Agent>(agent =>
        {
            agent.HasIndex(a => new { a.MemberId, a.ClientNameKey, a.MachineNameKey }).IsUnique().AreNullsDistinct(false);
            agent.HasOne(a => a.Member).WithMany().HasForeignKey(a => a.MemberId);
        });

        modelBuilder.Entity<Member>(member =>
        {
            member.HasIndex(m => m.EmailKey).IsUnique();
            member.Property(m => m.Email).HasMaxLength(320);
            member.Property(m => m.EmailKey).HasMaxLength(320);
            member.Property(m => m.DisplayName).HasMaxLength(50);
            member.HasOne<Member>().WithMany().HasForeignKey(m => m.InvitedByMemberId);
            member.Ignore(m => m.IsActive);
        });

        modelBuilder.Entity<SignInLink>(link =>
        {
            link.HasIndex(l => l.SecretHash).IsUnique();
            link.HasOne(l => l.Member).WithMany().HasForeignKey(l => l.MemberId);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.HasIndex(s => s.SecretHash).IsUnique();
            session.HasOne(s => s.Member).WithMany().HasForeignKey(s => s.MemberId);
        });

        modelBuilder.Entity<DeviceLogin>(login =>
        {
            login.HasIndex(l => l.DeviceSecretHash).IsUnique();

            // Unique for all time rather than only while pending: the code space (20^8) makes reuse unnecessary,
            // and an old code then reads as invalid instead of matching someone else's login.
            login.HasIndex(l => l.UserCode).IsUnique();
            login.Property(l => l.UserCode).HasMaxLength(8).IsFixedLength();
            login.Property(l => l.MachineName).HasMaxLength(100);
            login.HasOne<Member>().WithMany().HasForeignKey(l => l.ApprovedByMemberId);
            login.HasOne<ApiToken>().WithMany().HasForeignKey(l => l.TokenId);
        });

        modelBuilder.Entity<ApiToken>(token =>
        {
            token.HasIndex(t => t.SecretHash).IsUnique();
            token.HasIndex(t => t.MemberId);
            token.Property(t => t.MachineName).HasMaxLength(100);
            token.HasOne(t => t.Member).WithMany().HasForeignKey(t => t.MemberId);
        });

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
