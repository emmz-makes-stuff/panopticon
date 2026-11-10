using Microsoft.EntityFrameworkCore;
using Panopticon.Contracts;

namespace Panopticon.Server.Data;

public sealed class PanopticonDb(DbContextOptions<PanopticonDb> options) : DbContext(options)
{
    public DbSet<Repo> Repos => Set<Repo>();
    public DbSet<Change> Changes => Set<Change>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<ChangeAgent> ChangeAgents => Set<ChangeAgent>();
    public DbSet<ReportEvent> ReportEvents => Set<ReportEvent>();
    public DbSet<ReportDocument> ReportDocuments => Set<ReportDocument>();
    public DbSet<LatestDocument> ChangeDocuments => Set<LatestDocument>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<SignInLink> SignInLinks => Set<SignInLink>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<DeviceLogin> DeviceLogins => Set<DeviceLogin>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();
    public DbSet<ChangeProjectTag> ChangeProjectTags => Set<ChangeProjectTag>();
    public DbSet<ChangeAgentTag> ChangeAgentTags => Set<ChangeAgentTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Repo>(repo =>
        {
            repo.HasIndex(r => new { r.HostKey, r.OwnerKey, r.NameKey }).IsUnique();
            repo.HasMany(r => r.Changes).WithOne(c => c.Repo).HasForeignKey(c => c.RepoId);
        });

        modelBuilder.Entity<Change>(change =>
        {
            // change-documents-and-history D5: a name is unique among active changes only, so a name reused after an
            // archive gets a fresh row; an archive folder is unique per repo, which makes an archived report idempotent.
            // projects D1: both identity indexes include the change's scope, with nulls equal, so one scope's change
            // never shares a row with another's.
            change.HasIndex(c => new { c.RepoId, c.NameKey, c.ProjectId, c.OwnerMemberId })
                .IsUnique().AreNullsDistinct(false).HasFilter("archived_at IS NULL");
            change.HasIndex(c => new { c.RepoId, c.ArchiveFolder, c.ProjectId, c.OwnerMemberId })
                .IsUnique().AreNullsDistinct(false).HasFilter("archive_folder IS NOT NULL");
            change.HasIndex(c => c.LastReportedAt);
            change.HasIndex(c => c.ProjectId);
            change.HasIndex(c => c.OwnerMemberId);
            change.HasOne(c => c.Project).WithMany().HasForeignKey(c => c.ProjectId);
            change.HasOne(c => c.RetiredToProject).WithMany().HasForeignKey(c => c.RetiredToProjectId);
            change.HasOne<Member>().WithMany().HasForeignKey(c => c.OwnerMemberId);
            change.ToTable(t =>
            {
                t.HasCheckConstraint("ck_changes_one_scope", "project_id IS NULL OR owner_member_id IS NULL");

                // projects D4: a retired copy is archived with no folder.
                t.HasCheckConstraint(
                    "ck_changes_retired_unfoldered",
                    "retired_to_project_id IS NULL OR (archived_at IS NOT NULL AND archive_folder IS NULL)");
            });
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

        // projects D1.
        modelBuilder.Entity<Project>(project =>
        {
            project.HasIndex(p => p.CodeKey).IsUnique();
            project.Property(p => p.Name).HasMaxLength(100);
            project.Property(p => p.Code).HasMaxLength(10);
            project.Property(p => p.CodeKey).HasMaxLength(10);
            project.HasOne<Member>().WithMany().HasForeignKey(p => p.CreatedByMemberId);
            project.HasMany(p => p.Members).WithOne().HasForeignKey(m => m.ProjectId);
        });

        modelBuilder.Entity<ProjectMember>(membership =>
        {
            membership.HasKey(m => new { m.ProjectId, m.MemberId });
            membership.HasIndex(m => m.MemberId);
            membership.Property(m => m.Role).HasMaxLength(10);
            membership.HasOne(m => m.Member).WithMany().HasForeignKey(m => m.MemberId);
            membership.HasOne<Member>().WithMany().HasForeignKey(m => m.InvitedByMemberId);
            membership.ToTable(t => t.HasCheckConstraint("ck_project_members_role", "role IN ('owner', 'editor', 'viewer')"));
        });

        modelBuilder.Entity<ChangeAgent>(changeAgent =>
        {
            changeAgent.HasKey(ca => new { ca.ChangeId, ca.AgentId });
            changeAgent.HasOne(ca => ca.Agent).WithMany().HasForeignKey(ca => ca.AgentId);
            changeAgent.HasMany(ca => ca.Tags).WithOne()
                .HasForeignKey(t => new { t.ChangeId, t.AgentId }).OnDelete(DeleteBehavior.Cascade);
        });

        // tags D2. A tag's key and value are each at most TagText.MaxPartLength characters.
        modelBuilder.Entity<ProjectTag>(tag =>
        {
            tag.HasIndex(t => new { t.ProjectId, t.KeyKey, t.ValueKey }).IsUnique();
            tag.Property(t => t.Key).HasMaxLength(TagText.MaxPartLength);
            tag.Property(t => t.Value).HasMaxLength(TagText.MaxPartLength);
            tag.Property(t => t.KeyKey).HasMaxLength(TagText.MaxPartLength);
            tag.Property(t => t.ValueKey).HasMaxLength(TagText.MaxPartLength);
            tag.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChangeProjectTag>(changeTag =>
        {
            changeTag.HasKey(ct => new { ct.ChangeId, ct.TagId });
            changeTag.HasIndex(ct => ct.TagId);
            changeTag.HasOne<Change>().WithMany(c => c.ProjectTags).HasForeignKey(ct => ct.ChangeId).OnDelete(DeleteBehavior.Cascade);
            changeTag.HasOne(ct => ct.Tag).WithMany().HasForeignKey(ct => ct.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChangeAgentTag>(agentTag =>
        {
            agentTag.HasKey(t => new { t.ChangeId, t.AgentId, t.KeyKey, t.ValueKey });
            agentTag.HasIndex(t => new { t.KeyKey, t.ValueKey });
            agentTag.Property(t => t.Key).HasMaxLength(TagText.MaxPartLength);
            agentTag.Property(t => t.Value).HasMaxLength(TagText.MaxPartLength);
            agentTag.Property(t => t.KeyKey).HasMaxLength(TagText.MaxPartLength);
            agentTag.Property(t => t.ValueKey).HasMaxLength(TagText.MaxPartLength);
        });

        modelBuilder.Entity<ReportEvent>(reportEvent =>
        {
            reportEvent.Property(e => e.TasksSha256).HasMaxLength(64).IsFixedLength();
            reportEvent.HasIndex(e => new { e.ChangeId, e.ReceivedAt });
            reportEvent.HasOne<Change>().WithMany().HasForeignKey(e => e.ChangeId);
            reportEvent.HasOne<Agent>().WithMany().HasForeignKey(e => e.AgentId);
            reportEvent.OwnsMany(e => e.Sections, sections => sections.ToJson("sections"));

            // change-metrics D3: the backfill looks for the rows it hasn't filled in yet.
            reportEvent.HasIndex(e => e.Id).HasFilter("overall_total IS NULL").HasDatabaseName("ix_report_events_progress_pending");
        });

        modelBuilder.Entity<ReportDocument>(document =>
        {
            document.ToTable("report_documents");
            document.HasKey(d => new { d.ReportEventId, d.Path });
            document.Property(d => d.Path).HasMaxLength(512);
            document.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength();
            document.HasOne<ReportEvent>().WithMany().HasForeignKey(d => d.ReportEventId);
        });

        modelBuilder.Entity<LatestDocument>(document =>
        {
            document.ToTable("change_documents");
            document.HasKey(d => new { d.ChangeId, d.Path });
            document.Property(d => d.Path).HasMaxLength(512);
            document.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength();
            document.HasOne<Change>().WithMany().HasForeignKey(d => d.ChangeId);
        });
    }
}
