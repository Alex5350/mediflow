namespace MediFlow.Domain.UnitTests.Claims;

using MediFlow.Domain.Accumulators;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Claims.Adjudication;
using MediFlow.Domain.Enrollment;
using MediFlow.Domain.Fees;
using MediFlow.Domain.Members;
using MediFlow.Domain.Plans;
using Xunit;

public class ClaimRuleTests
{
    private static readonly DateOnly ServiceDate = new(2026, 6, 15);

    private static AdjudicationRequest Request(
        DateOnly? serviceDate = null,
        EnrollmentStatus coverageStatus = EnrollmentStatus.Active,
        DateOnly? coverageStart = null,
        DateOnly? coverageEnd = null,
        PriorClaimFingerprint[]? priors = null) =>
        new(
            new Claim
            {
                ClaimNumber = "CLM-2026-000001",
                RenderingProviderNpi = "1234567893",
                ServiceDate = serviceDate ?? ServiceDate,
                ReceivedAtUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
                Lines =
                [
                    new ClaimLine { Sequence = 1, ProcedureCode = "99214", ChargeCents = 20000 },
                ],
            },
            new Member { Mbi = "1EG4TE5MK73", FirstName = "A", LastName = "B", StateCode = "TX" },
            new Plan { PlanCode = "MFP-2601", Name = "P", Carrier = "C", Type = PlanType.MedicareAdvantage, ContractYear = 2026, DeductibleCents = 17500, CoinsurancePercent = 20, OopMaxCents = 550000 },
            coverageStatus == EnrollmentStatus.Active
                ? new EnrollmentApplication
                {
                    ApplicationNumber = "ENR-2025-000001",
                    MemberId = 1,
                    PlanId = 1,
                    Status = coverageStatus,
                    RequestedEffectiveDate = coverageStart ?? new DateOnly(2026, 1, 1),
                    CancelledEffectiveDate = coverageEnd,
                }
                : new EnrollmentApplication
                {
                    ApplicationNumber = "ENR-2025-000001",
                    MemberId = 1,
                    PlanId = 1,
                    Status = coverageStatus,
                    RequestedEffectiveDate = coverageStart ?? new DateOnly(2026, 1, 1),
                },
            new Dictionary<string, ProcedureFee>(),
            new BenefitAccumulator(),
            priors ?? []);

    [Fact]
    public void Timeliness_denies_claims_older_than_one_year()
    {
        var rule = new FilingTimelinessRule();
        Assert.Equal(DenialCode.TimelyFiling, rule.Evaluate(Request(serviceDate: new DateOnly(2024, 6, 1))));
        Assert.Null(rule.Evaluate(Request(serviceDate: new DateOnly(2025, 7, 2))));   // exactly inside 365d of 2026-07-01
        Assert.Equal(DenialCode.TimelyFiling, rule.Evaluate(Request(serviceDate: new DateOnly(2025, 6, 15)))); // past the limit
    }

    [Fact]
    public void Coverage_requires_active_enrollment_spanning_the_service_date()
    {
        var rule = new CoverageRule();
        Assert.Null(rule.Evaluate(Request()));
        Assert.Equal(DenialCode.CoverageTerminated, rule.Evaluate(Request(coverageStatus: EnrollmentStatus.Approved)));
        Assert.Equal(DenialCode.CoverageTerminated, rule.Evaluate(Request(coverageStart: new DateOnly(2026, 7, 1))));
        Assert.Equal(DenialCode.CoverageTerminated, rule.Evaluate(Request(coverageEnd: new DateOnly(2026, 6, 1))));
        Assert.Null(rule.Evaluate(Request(coverageEnd: new DateOnly(2026, 6, 30))));  // service on the last covered day
    }

    [Fact]
    public void Duplicate_matches_provider_date_and_procedure()
    {
        var rule = new DuplicateClaimRule();
        var prior = new PriorClaimFingerprint("1234567893", ServiceDate, "99214");

        Assert.Equal(DenialCode.DuplicateClaim, rule.Evaluate(Request(priors: [prior])));

        var differentProvider = new PriorClaimFingerprint("9999999996", ServiceDate, "99214");
        var differentDate = new PriorClaimFingerprint("1234567893", ServiceDate.AddDays(1), "99214");
        var differentProcedure = new PriorClaimFingerprint("1234567893", ServiceDate, "80053");
        Assert.Null(rule.Evaluate(Request(priors: [differentProvider, differentDate, differentProcedure])));
    }
}

public class AdjudicationEngineTests
{
    private static readonly Dictionary<string, ProcedureFee> Fees = new()
    {
        ["99214"] = new ProcedureFee { ProcedureCode = "99214", Description = "Office visit", AllowedCents = 17400, IsCovered = true, EffectiveYear = 2026 },
        ["80053"] = new ProcedureFee { ProcedureCode = "80053", Description = "Metabolic panel", AllowedCents = 2900, IsCovered = true, EffectiveYear = 2026 },
        ["S9994"] = new ProcedureFee { ProcedureCode = "S9994", Description = "Concierge fee", AllowedCents = 60000, IsCovered = false, EffectiveYear = 2026 },
    };

    private static readonly Plan Plan = new()
    {
        PlanCode = "MFP-2601",
        Name = "P",
        Carrier = "C",
        Type = PlanType.MedicareAdvantage,
        ContractYear = 2026,
        DeductibleCents = 20000,
        CoinsurancePercent = 20,
        OopMaxCents = 100000,
    };

    private static AdjudicationEngine Engine() => new(
    [
        (IAdjudicationClaimRule)new FilingTimelinessRule(),
        new CoverageRule(),
        new DuplicateClaimRule(),
    ]);

    private static AdjudicationRequest Request(
        Claim claim,
        EnrollmentStatus coverageStatus = EnrollmentStatus.Active,
        BenefitAccumulator? accumulator = null,
        PriorClaimFingerprint[]? priors = null) =>
        new(claim,
            new Member { Mbi = "1EG4TE5MK73", FirstName = "A", LastName = "B", StateCode = "TX" },
            Plan,
            new EnrollmentApplication
            {
                ApplicationNumber = "ENR-2025-000001",
                MemberId = 1,
                PlanId = 1,
                Status = coverageStatus,
                RequestedEffectiveDate = new DateOnly(2026, 1, 1),
            },
            Fees,
            accumulator ?? new BenefitAccumulator { MemberId = 1, BenefitYear = 2026 },
            priors ?? []);

    private static Claim NewClaim(params (string Code, int Charge)[] lines) => new()
    {
        ClaimNumber = "CLM-2026-000001",
        RenderingProviderNpi = "1234567893",
        ServiceDate = new DateOnly(2026, 6, 15),
        ReceivedAtUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
        Lines = [.. lines.Select((l, i) => new ClaimLine { Sequence = i + 1, ProcedureCode = l.Code, ChargeCents = l.Charge })],
    };

    [Fact]
    public void First_claim_of_the_year_pays_deductible_then_coinsurance()
    {
        // Line: allowed $174.00 — all $200 deductible applies first, but the deductible
        // is $200 so member owes the full allowed amount, plan pays $0.
        var result = Engine().Adjudicate(Request(NewClaim(("99214", 20000))));
        var line = Assert.Single(result.Lines);
        Assert.Equal(ClaimStatus.Paid, result.Status);
        Assert.Equal(17400, line.AllowedCents);
        Assert.Equal(0, line.PlanPaidCents);
        Assert.Equal(17400, line.MemberOwesCents);
        Assert.Equal(DenialCode.Deductible, line.DenialCode);
        Assert.Equal(17400, result.NewDeductibleMetCents);
        Assert.Equal(17400, result.NewOopMetCents);
    }

    [Fact]
    public void Deductible_consumes_across_lines_in_sequence()
    {
        // Two lines: allowed $174.00 + $29.00 = $203.00 against a $200 deductible.
        // Line 1: member owes all $174.00 (deductible). Line 2: remaining $26.00
        // deductible + 20% of $3.00 = $26.60. Member total $200.60, plan $2.40.
        var result = Engine().Adjudicate(Request(NewClaim(("99214", 20000), ("80053", 4500))));
        Assert.Equal(20300, result.TotalAllowedCents);
        Assert.Equal(20000, result.NewDeductibleMetCents);   // exactly met
        Assert.Equal(20060, result.TotalMemberOwesCents);
        Assert.Equal(240, result.TotalPlanPaidCents);
        Assert.Equal(20060, result.NewOopMetCents);
    }

    [Fact]
    public void Oop_max_caps_member_exposure()
    {
        // Accumulators already at the $1,000 OOP max → plan pays everything allowed.
        var accumulator = new BenefitAccumulator { MemberId = 1, BenefitYear = 2026, DeductibleMetCents = 20000, OopMetCents = 100000 };
        var result = Engine().Adjudicate(Request(NewClaim(("99214", 20000)), accumulator: accumulator));
        var line = Assert.Single(result.Lines);
        Assert.Equal(17400, line.PlanPaidCents);
        Assert.Equal(0, line.MemberOwesCents);
        Assert.Equal(100000, result.NewOopMetCents);
    }

    [Fact]
    public void Non_covered_line_denies_with_co96_but_rest_pays()
    {
        // OOP met 97000 of 100000 → remaining member exposure is 3000c ($30),
        // which caps the $34.80 coinsurance on the covered line.
        var result = Engine().Adjudicate(Request(NewClaim(("99214", 20000), ("S9994", 65000)), accumulator: new BenefitAccumulator
        {
            MemberId = 1,
            BenefitYear = 2026,
            DeductibleMetCents = 20000,
            OopMetCents = 97000,
        }));
        Assert.Equal(ClaimStatus.Paid, result.Status);
        Assert.Equal(DenialCode.NonCoveredService, result.Lines[1].DenialCode);
        Assert.Equal(0, result.Lines[1].AllowedCents);
        Assert.Equal(17400, result.Lines[0].AllowedCents);
        Assert.Equal(3000, result.Lines[0].MemberOwesCents);
        Assert.Equal(14400, result.Lines[0].PlanPaidCents);
    }

    [Fact]
    public void Unknown_procedure_code_is_non_covered()
    {
        var result = Engine().Adjudicate(Request(NewClaim(("XXXXX", 10000))));
        Assert.Equal(DenialCode.NonCoveredService, result.Lines[0].DenialCode);
    }

    [Fact]
    public void Timeliness_short_circuits_before_coverage()
    {
        // Old service date AND no coverage: filing rule must win (rule order is semantic).
        var claim = NewClaim(("99214", 20000));
        claim.ServiceDate = new DateOnly(2024, 1, 10);
        var result = Engine().Adjudicate(Request(claim, coverageStatus: EnrollmentStatus.Approved));
        Assert.Equal(ClaimStatus.Denied, result.Status);
        Assert.Equal(DenialCode.TimelyFiling, result.ClaimDenialCode);
        Assert.Equal(0, result.TotalPlanPaidCents);
        Assert.Equal(0, result.TotalMemberOwesCents);
    }

    [Fact]
    public void Whole_claim_denial_zeroes_every_line()
    {
        var prior = new PriorClaimFingerprint("1234567893", new DateOnly(2026, 6, 15), "99214");
        var result = Engine().Adjudicate(Request(NewClaim(("99214", 20000), ("80053", 4500)), priors: [prior]));
        Assert.Equal(ClaimStatus.Denied, result.Status);
        Assert.Equal(DenialCode.DuplicateClaim, result.ClaimDenialCode);
        Assert.All(result.Lines, line =>
        {
            Assert.Equal(0, line.PlanPaidCents);
            Assert.Equal(0, line.MemberOwesCents);
            Assert.Equal(DenialCode.DuplicateClaim, line.DenialCode);
        });
    }

    [Fact]
    public void Charge_above_fee_schedule_is_capped_at_allowed()
    {
        var result = Engine().Adjudicate(Request(NewClaim(("99214", 99000)), accumulator: new BenefitAccumulator
        {
            MemberId = 1,
            BenefitYear = 2026,
            DeductibleMetCents = 20000,
            OopMetCents = 90000,
        }));
        Assert.Equal(17400, result.Lines[0].AllowedCents);
    }
}

public class ClaimSubmissionRulesTests
{
    [Fact]
    public void Invalid_npi_is_rejected()
    {
        var violations = ClaimSubmissionRules.Validate("1234567890", 1, 1, new DateOnly(2026, 8, 1), DateTime.UtcNow,
            [("99214", 15000)]);
        Assert.Contains(violations, v => v.Code == "NPI_INVALID");
    }

    [Fact]
    public void Future_service_date_is_rejected()
    {
        var violations = ClaimSubmissionRules.Validate("1234567893", 1, 1, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3),
            DateTime.UtcNow, [("99214", 15000)]);
        Assert.Contains(violations, v => v.Code == "SERVICE_DATE_FUTURE");
    }

    [Fact]
    public void Empty_lines_and_bad_charges_are_rejected()
    {
        var violations = ClaimSubmissionRules.Validate("1234567893", 1, 1, new DateOnly(2026, 8, 1), DateTime.UtcNow, []);
        Assert.Contains(violations, v => v.Code == "LINES_REQUIRED");

        var badLines = ClaimSubmissionRules.Validate("1234567893", 1, 1, new DateOnly(2026, 8, 1), DateTime.UtcNow,
            [("toolong!", 100), ("99213", 0)]);
        Assert.Contains(badLines, v => v.Code.Contains("CODE_INVALID"));
        Assert.Contains(badLines, v => v.Code.Contains("CHARGE_INVALID"));
    }

    [Fact]
    public void Clean_submission_has_no_violations()
    {
        var violations = ClaimSubmissionRules.Validate("1234567893", 42, 7, new DateOnly(2026, 8, 1), DateTime.UtcNow,
            [("99213", 11800), ("G2211", 1900)]);
        Assert.Empty(violations);
    }
}
