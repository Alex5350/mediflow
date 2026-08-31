namespace MediFlow.Domain.UnitTests.Enrollment;

using MediFlow.Domain.Enrollment;
using MediFlow.Domain.Members;
using MediFlow.Domain.Plans;
using Xunit;

public class EnrollmentRulesTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 31);
    private static readonly DateTime SubmittedAt = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static Member EntitledMember(DateOnly partB) => new()
    {
        Mbi = "1EG4TE5MK73",
        FirstName = "Test",
        LastName = "Beneficiary",
        DateOfBirth = new(1950, 1, 1),
        StateCode = "TX",
        PartAEffective = partB.AddMonths(-1),
        PartBEffective = partB,
    };

    private static Plan Plan2026(PlanType type = PlanType.MedicareAdvantage, bool fiveStar = false) => new()
    {
        PlanCode = fiveStar ? "MFP-2650" : "MFP-2601",
        Name = "Test Plan",
        Carrier = "Cascade Mutual Health",
        Type = type,
        ContractYear = 2026,
        MonthlyPremiumCents = 1900,
        DeductibleCents = 17500,
        CoinsurancePercent = 20,
        OopMaxCents = 550000,
        IsFiveStar = fiveStar,
    };

    [Fact]
    public void Icep_window_around_part_b_entitlement_is_eligible()
    {
        var member = EntitledMember(new DateOnly(2026, 9, 1));
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 1), SepReason.None, [], AsOf, SubmittedAt);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Sep_allows_first_of_next_month()
    {
        var member = EntitledMember(new DateOnly(2025, 6, 1));
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 1), SepReason.Moved, [], AsOf, SubmittedAt);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Sep_rejects_any_other_effective_date()
    {
        var member = EntitledMember(new DateOnly(2025, 6, 1));
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 10, 1), SepReason.Moved, [], AsOf, SubmittedAt);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Code == EnrollmentViolation.OutsideEnrollmentWindow);
    }

    [Theory]
    [InlineData(10, 14)]  // day before AEP start
    [InlineData(12, 8)]   // day after AEP end
    public void AEP_window_is_oct15_dec7(int month, int day)
    {
        Assert.False(EnrollmentRules.IsWithinAep(new DateOnly(2026, month, day), out _));
        Assert.True(EnrollmentRules.IsWithinAep(new DateOnly(2026, 10, 15), out var effectiveYear));
        Assert.Equal(2027, effectiveYear);
        Assert.True(EnrollmentRules.IsWithinAep(new DateOnly(2026, 12, 7), out _));
    }

    [Fact]
    public void AEP_submission_effective_jan1_is_eligible()
    {
        var member = EntitledMember(new DateOnly(2025, 6, 1));
        var plan2027 = Plan2026();
        plan2027.ContractYear = 2027;   // AEP in late 2026 targets next year's products
        var result = EnrollmentRules.Validate(member, plan2027, new DateOnly(2027, 1, 1), SepReason.None, [],
            new DateOnly(2026, 11, 1), new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void No_part_b_entitlement_is_rejected()
    {
        var member = EntitledMember(new DateOnly(2026, 9, 15));
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 1), SepReason.None, [], AsOf, SubmittedAt);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Code == EnrollmentViolation.PartBNotEffective);
    }

    [Fact]
    public void Active_same_type_enrollment_blocks_unless_five_star()
    {
        var member = EntitledMember(new DateOnly(2025, 6, 1));
        var existing = new EnrollmentApplication
        {
            ApplicationNumber = "ENR-2025-000001",
            MemberId = 1,
            PlanId = 1,
            Status = EnrollmentStatus.Active,
            RequestedEffectiveDate = new(2026, 1, 1),
            Plan = Plan2026(),
        };

        var blocked = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 1), SepReason.Moved, [existing], AsOf, SubmittedAt);
        Assert.False(blocked.IsValid);
        Assert.Contains(blocked.Violations, v => v.Code == EnrollmentViolation.AlreadyEnrolledSameType);

        var fiveStar = EnrollmentRules.Validate(member, Plan2026(fiveStar: true), new DateOnly(2026, 9, 1), SepReason.FiveStar, [existing], AsOf, SubmittedAt);
        Assert.True(fiveStar.IsValid);
    }

    [Fact]
    public void Different_plan_type_does_not_block()
    {
        var member = EntitledMember(new DateOnly(2025, 6, 1));
        var existing = new EnrollmentApplication
        {
            ApplicationNumber = "ENR-2025-000002",
            MemberId = 1,
            PlanId = 2,
            Status = EnrollmentStatus.Active,
            RequestedEffectiveDate = new(2026, 1, 1),
            Plan = Plan2026(PlanType.PrescriptionDrug),
        };

        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 1), SepReason.Moved, [existing], AsOf, SubmittedAt);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Plan_year_must_match_effective_year()
    {
        var member = EntitledMember(new DateOnly(2025, 6, 1));
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2027, 9, 1), SepReason.Moved, [], AsOf, SubmittedAt);
        Assert.Contains(result.Violations, v => v.Code == EnrollmentViolation.PlanNotOfferedForYear);
    }

    [Fact]
    public void Effective_date_must_be_first_of_month()
    {
        var member = EntitledMember(new DateOnly(2026, 9, 1));
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 15), SepReason.None, [], AsOf, SubmittedAt);
        Assert.Contains(result.Violations, v => v.Code == EnrollmentViolation.EffectiveDateNotFirstOfMonth);
    }

    [Fact]
    public void Multiple_violations_are_reported_together()
    {
        var member = new Member
        {
            Mbi = "1EG4TE5MK73",
            FirstName = "No",
            LastName = "Entitlement",
            DateOfBirth = new(1951, 2, 3),
            StateCode = "OH",
            PartBEffective = null,
        };
        var result = EnrollmentRules.Validate(member, Plan2026(), new DateOnly(2026, 9, 15), SepReason.None, [], AsOf, SubmittedAt);
        Assert.False(result.IsValid);
        Assert.True(result.Violations.Count >= 2);
    }
}

public class EnrollmentStateMachineTests
{
    [Theory]
    [InlineData(EnrollmentStatus.Draft, EnrollmentStatus.Submitted, true)]
    [InlineData(EnrollmentStatus.Submitted, EnrollmentStatus.PendingVerification, true)]
    [InlineData(EnrollmentStatus.PendingVerification, EnrollmentStatus.Approved, true)]
    [InlineData(EnrollmentStatus.PendingVerification, EnrollmentStatus.Denied, true)]
    [InlineData(EnrollmentStatus.Approved, EnrollmentStatus.Active, true)]
    [InlineData(EnrollmentStatus.Active, EnrollmentStatus.Cancelled, true)]
    [InlineData(EnrollmentStatus.Draft, EnrollmentStatus.Approved, false)]   // skip the queue
    [InlineData(EnrollmentStatus.Denied, EnrollmentStatus.Submitted, false)] // denied is terminal
    [InlineData(EnrollmentStatus.Cancelled, EnrollmentStatus.Submitted, false)]
    [InlineData(EnrollmentStatus.Active, EnrollmentStatus.Denied, false)]
    public void Transitions(EnrollmentStatus from, EnrollmentStatus to, bool allowed)
    {
        Assert.Equal(allowed, EnrollmentStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void TryTransition_mutates_only_when_legal()
    {
        var application = new EnrollmentApplication
        {
            ApplicationNumber = "ENR-2026-000001",
            MemberId = 1,
            PlanId = 1,
            Status = EnrollmentStatus.PendingVerification,
        };

        Assert.True(EnrollmentStateMachine.TryTransition(application, EnrollmentStatus.Approved));
        Assert.Equal(EnrollmentStatus.Approved, application.Status);

        Assert.False(EnrollmentStateMachine.TryTransition(application, EnrollmentStatus.PendingVerification));
        Assert.Equal(EnrollmentStatus.Approved, application.Status);
    }
}
