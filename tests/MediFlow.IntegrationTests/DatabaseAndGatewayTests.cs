namespace MediFlow.IntegrationTests;

using MediFlow.Domain.Claims.Adjudication;
using MediFlow.Infrastructure.Claims;
using MediFlow.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Collection("database")]
public sealed class StoredProcTests(TestDatabaseFixture db)
{
    [Fact]
    public async Task Migrations_and_all_procedures_are_applied()
    {
        await using var connection = (SqlConnection)await db.Resolve<IDbConnectionFactory>().CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.procedures WHERE name LIKE 'usp[_]%'
            """;
        var count = (int)(await command.ExecuteScalarAsync() ?? 0);
        Assert.True(count >= 8, $"expected >= 8 procs, found {count}");
    }

    [Fact]
    public async Task Seed_populated_members_plans_and_claims()
    {
        var context = db.NewDbContext();
        await using (context)
        {
            Assert.True(await context.Members.CountAsync() > 100);
            Assert.True(await context.Plans.CountAsync() >= 12);
            Assert.True(await context.Claims.CountAsync() > 400);
            Assert.True(await context.Outbox.CountAsync(o => o.CompletedAtUtc == null) >= 4); // seeded open/dead-letter queue
        }
    }

    [Fact]
    public async Task SearchMembers_matches_name_prefix_and_paging_totals()
    {
        var store = db.Resolve<IReadStore>();
        var firstPage = await store.SearchMembersAsync("Whitfield", 1, 5);
        Assert.True(firstPage.Total >= 1);
        Assert.All(firstPage.Items, m => Assert.Equal("Whitfield", m.LastName, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ClaimsQueue_filters_by_status()
    {
        var store = db.Resolve<IReadStore>();
        var queue = await store.ClaimsQueueAsync([Domain.Claims.ClaimStatus.Paid], null, null, 1, 10);
        Assert.True(queue.Total > 0);
        Assert.All(queue.Items, c => Assert.Equal(Domain.Claims.ClaimStatus.Paid, (Domain.Claims.ClaimStatus)c.Status));
    }

    [Fact]
    public async Task Member360_returns_three_result_sets()
    {
        var context = db.NewDbContext();
        await using (context)
        {
            var memberWithClaims = await context.Claims
                .AsNoTracking()
                .Select(c => c.MemberId)
                .FirstAsync();
            var store = db.Resolve<IReadStore>();
            var view = await store.GetMember360Async(memberWithClaims);
            Assert.NotNull(view?.Header);
            Assert.Equal(memberWithClaims, view!.Header.Id);
            Assert.True(view.Claims.Count > 0);
        }
    }

    [Fact]
    public async Task Rollups_and_dashboard_stats_return_data()
    {
        var store = db.Resolve<IReadStore>();
        var stats = await store.DashboardStatsAsync();
        Assert.True(stats.EnrollmentsActive > 50);
        Assert.True(stats.YtdPlanPaidCents > 0);

        var denials = await store.DenialRollupAsync(2026);
        Assert.NotEmpty(denials);

        var plans = await store.PlanEnrollmentSummaryAsync(2026);
        Assert.True(plans.Count >= 12);
    }
}

[Collection("database")]
public sealed class AdjudicationGatewayTests(TestDatabaseFixture db)
{
    [Fact]
    public async Task Lease_commit_and_exclusive_lease_work_end_to_end()
    {
        // Fresh member + plan + claim via the real intake path.
        var intake = db.Resolve<IClaimIntakeService>();
        var context = db.NewDbContext();
        await using (context)
        {
            var member = NewEntitledMember(context);
            var plan = await context.Plans.AsNoTracking().FirstAsync(p => p.ContractYear == 2026 && p.Type == Domain.Plans.PlanType.MedicareAdvantage);
            AddActiveEnrollment(context, member.Id, plan.Id);
            var result = await intake.SubmitClaimAsync(new Contracts.Claims.SubmitClaimRequest
            {
                MemberId = member.Id,
                PlanId = plan.Id,
                RenderingProviderNpi = "1234567893",
                Type = 0,
                ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
                Lines = [new Contracts.Claims.SubmitClaimLine { ProcedureCode = "99214", ChargeCents = 20000 }],
            }, actor: "it");
            Assert.True(result.Accepted, string.Join("; ", result.Violations.Select(v => v.Message)));

            var gateway = db.Resolve<IAdjudicationGateway>();
            var runner = db.Resolve<IClaimAdjudicationRunner>();
            var engine = db.Resolve<AdjudicationEngine>();

            var leaseA = Guid.NewGuid();
            var leased = await gateway.LeaseNextClaimsAsync(100, TimeSpan.FromMinutes(2), leaseA);
            Assert.Contains(result.ClaimId!.Value, leased);

            // A second lease must not pick the same claim while the first holds it.
            var leaseB = Guid.NewGuid();
            var leasedB = await gateway.LeaseNextClaimsAsync(100, TimeSpan.FromMinutes(2), leaseB);
            Assert.DoesNotContain(result.ClaimId!.Value, leasedB);

            // Committing with the WRONG lease is rejected (SQL 51002).
            var work = await runner.LoadForAdjudicationAsync(result.ClaimId!.Value);
            var decision = engine.Adjudicate(work!.Request);
            await Assert.ThrowsAsync<SqlException>(() =>
                gateway.CommitAdjudicationAsync(result.ClaimId.Value, leaseB, decision));

            // Committing with the right lease persists the full decision atomically.
            await gateway.CommitAdjudicationAsync(result.ClaimId.Value, leaseA, decision);

            var committed = await context.Claims
                .AsNoTracking()
                .Include(c => c.Lines)
                .FirstAsync(c => c.Id == result.ClaimId.Value);
            Assert.Equal(Domain.Claims.ClaimStatus.Paid, committed.Status);
            Assert.Equal(decision.TotalPlanPaidCents, committed.TotalPlanPaidCents);
            Assert.All(committed.Lines, l => Assert.NotNull(l.PlanPaidCents));
        }
    }

    [Fact]
    public async Task FailLease_requeues_with_backoff()
    {
        var intake = db.Resolve<IClaimIntakeService>();
        var context = db.NewDbContext();
        await using (context)
        {
            var member = NewEntitledMember(context);
            var plan = await context.Plans.AsNoTracking().FirstAsync(p => p.ContractYear == 2026 && p.Type == Domain.Plans.PlanType.MedicareAdvantage);
            var result = await intake.SubmitClaimAsync(new Contracts.Claims.SubmitClaimRequest
            {
                MemberId = member.Id,
                PlanId = plan.Id,
                RenderingProviderNpi = "1234567893",
                Type = 0,
                ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5),
                Lines = [new Contracts.Claims.SubmitClaimLine { ProcedureCode = "80053", ChargeCents = 5000 }],
            }, actor: "it");
            Assert.True(result.Accepted);

            var gateway = db.Resolve<IAdjudicationGateway>();
            var lease = Guid.NewGuid();
            var leased = await gateway.LeaseNextClaimsAsync(10, TimeSpan.FromMinutes(2), lease);
            Assert.Contains(result.ClaimId!.Value, leased);

            await gateway.FailLeaseAsync(result.ClaimId.Value, lease, "simulated failure", TimeSpan.FromSeconds(30));

            var claim = await context.Claims.AsNoTracking().FirstAsync(c => c.Id == result.ClaimId.Value);
            Assert.Equal(Domain.Claims.ClaimStatus.Received, claim.Status);
            Assert.Null(claim.LeaseToken);

            // The outbox message is held back by the backoff window…
            var store = db.Resolve<IReadStore>();
            var immediate = await gateway.LeaseNextClaimsAsync(100, TimeSpan.FromMinutes(2), Guid.NewGuid());
            Assert.DoesNotContain(result.ClaimId.Value, immediate);
        }
    }

    private static void AddActiveEnrollment(MediFlow.Infrastructure.Persistence.MediFlowDbContext context, int memberId, int planId)
    {
        context.Enrollments.Add(new Domain.Enrollment.EnrollmentApplication
        {
            ApplicationNumber = $"ENR-IT-{Guid.NewGuid().ToString()[..8]}",
            MemberId = memberId,
            PlanId = planId,
            Status = Domain.Enrollment.EnrollmentStatus.Active,
            SepReason = Domain.Enrollment.SepReason.None,
            RequestedEffectiveDate = new DateOnly(2026, 1, 1),
            SubmittedAtUtc = new DateTime(2025, 11, 1, 9, 0, 0, DateTimeKind.Utc),
            DecidedAtUtc = new DateTime(2025, 12, 1, 9, 0, 0, DateTimeKind.Utc),
        });
        context.SaveChanges();
    }

    private static Domain.Members.Member NewEntitledMember(MediFlow.Infrastructure.Persistence.MediFlowDbContext context)
    {
        var member = new Domain.Members.Member
        {
            Mbi = Guid.NewGuid().ToString("N")[..11].ToUpperInvariant(),
            FirstName = "Test",
            LastName = "Leasing",
            DateOfBirth = new DateOnly(1949, 5, 20),
            StateCode = "TX",
            PartAEffective = new DateOnly(2024, 1, 1),
            PartBEffective = new DateOnly(2024, 1, 1),
            CreatedAtUtc = DateTime.UtcNow,
        };
        context.Members.Add(member);
        context.SaveChanges();
        return member;
    }
}
