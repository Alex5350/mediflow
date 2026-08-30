namespace MediFlow.Infrastructure.Persistence;

using MediFlow.Domain.Accumulators;
using MediFlow.Domain.Auditing;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Enrollment;
using MediFlow.Domain.Fees;
using MediFlow.Domain.Members;
using MediFlow.Domain.Messaging;
using MediFlow.Domain.Plans;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core context for the MediFlow operational store. Writes go through EF;
/// hot read paths (search, queues, rollups) go through the stored-procedure
/// Dapper layer in <see cref="DapperReadStore"/> (see ADR 0003).
/// </summary>
public sealed class MediFlowDbContext(DbContextOptions<MediFlowDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<EnrollmentApplication> Enrollments => Set<EnrollmentApplication>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimLine> ClaimLines => Set<ClaimLine>();
    public DbSet<ProcedureFee> ProcedureFees => Set<ProcedureFee>();
    public DbSet<BenefitAccumulator> Accumulators => Set<BenefitAccumulator>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<Member>(e =>
        {
            e.ToTable("Members");
            e.HasIndex(m => m.Mbi).IsUnique();
            e.HasIndex(m => new { m.LastName, m.FirstName });
            e.Property(m => m.Mbi).HasMaxLength(11).IsFixedLength();
            e.Property(m => m.FirstName).HasMaxLength(64);
            e.Property(m => m.LastName).HasMaxLength(64);
            e.Property(m => m.StateCode).HasMaxLength(2).IsFixedLength();
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("Plans");
            e.HasIndex(p => new { p.PlanCode, p.ContractYear }).IsUnique();
            e.Property(p => p.PlanCode).HasMaxLength(16);
            e.Property(p => p.Name).HasMaxLength(128);
            e.Property(p => p.Carrier).HasMaxLength(96);
        });

        modelBuilder.Entity<EnrollmentApplication>(e =>
        {
            e.ToTable("Enrollments");
            e.HasIndex(a => a.ApplicationNumber).IsUnique();
            e.HasIndex(a => new { a.MemberId, a.Status });
            e.HasOne(a => a.Member).WithMany().HasForeignKey(a => a.MemberId);
            e.HasOne(a => a.Plan).WithMany().HasForeignKey(a => a.PlanId);
            e.Property(a => a.ApplicationNumber).HasMaxLength(20);
            e.Property(a => a.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Claim>(e =>
        {
            e.ToTable("Claims");
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.HasIndex(c => c.MemberId);
            e.HasIndex(c => c.RenderingProviderNpi);
            // Filtered index for the work queue: only Received/Adjudicating rows are scanned.
            e.HasIndex(c => new { c.Status, c.ReceivedAtUtc })
                .HasFilter("[Status] IN (0, 1)")
                .HasDatabaseName("IX_Claims_Queue");
            e.HasIndex(c => new { c.MemberId, c.ServiceDate });
            e.HasOne(c => c.Member).WithMany().HasForeignKey(c => c.MemberId);
            e.HasOne(c => c.Plan).WithMany().HasForeignKey(c => c.PlanId);
            e.Property(c => c.ClaimNumber).HasMaxLength(20);
            e.Property(c => c.RenderingProviderNpi).HasMaxLength(10).IsFixedLength();
        });

        modelBuilder.Entity<ClaimLine>(e =>
        {
            e.ToTable("ClaimLines");
            e.HasIndex(l => new { l.ClaimId, l.Sequence }).IsUnique();
            e.HasOne(l => l.Claim).WithMany(c => c.Lines).HasForeignKey(l => l.ClaimId);
            e.Property(l => l.ProcedureCode).HasMaxLength(5);
        });

        modelBuilder.Entity<ProcedureFee>(e =>
        {
            e.ToTable("ProcedureFees");
            e.HasIndex(f => new { f.ProcedureCode, f.EffectiveYear }).IsUnique();
            e.Property(f => f.ProcedureCode).HasMaxLength(5);
            e.Property(f => f.Description).HasMaxLength(160);
        });

        modelBuilder.Entity<BenefitAccumulator>(e =>
        {
            e.ToTable("BenefitAccumulators");
            e.HasIndex(a => new { a.MemberId, a.BenefitYear }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("Outbox");
            // The worker's lease scan only ever reads incomplete messages.
            e.HasIndex(o => new { o.Type, o.AvailableAtUtc })
                .HasFilter("[CompletedAtUtc] IS NULL")
                .HasDatabaseName("IX_Outbox_Pending");
            e.Property(o => o.Type).HasMaxLength(64);
            e.Property(o => o.LastError).HasMaxLength(512);
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditEntries");
            e.HasIndex(a => new { a.EntityType, a.EntityKey, a.AtUtc });
            e.Property(a => a.EntityType).HasMaxLength(64);
            e.Property(a => a.EntityKey).HasMaxLength(20);
            e.Property(a => a.Action).HasMaxLength(32);
            e.Property(a => a.Actor).HasMaxLength(64);
        });
    }
}
