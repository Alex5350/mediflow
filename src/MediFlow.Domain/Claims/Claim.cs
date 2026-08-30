namespace MediFlow.Domain.Claims;

using Enrollment;
using Fees;
using Members;
using Plans;

/// <summary>A healthcare claim submitted for adjudication.</summary>
public sealed class Claim
{
    public int Id { get; set; }

    /// <summary>Business key, e.g. CLM-2026-000421.</summary>
    public required string ClaimNumber { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    /// <summary>Enrollment application that authorized the coverage, for audit.</summary>
    public int? EnrollmentApplicationId { get; set; }

    public ClaimType Type { get; set; }

    /// <summary>NPI of the rendering provider (validated with the Luhn check).</summary>
    public required string RenderingProviderNpi { get; set; }

    /// <summary>Date the service was rendered — drives timeliness and coverage rules.</summary>
    public DateOnly ServiceDate { get; set; }

    /// <summary>Total billed across all lines, in cents.</summary>
    public int TotalChargeCents { get; set; }

    public ClaimStatus Status { get; set; } = ClaimStatus.Received;

    public DateTime ReceivedAtUtc { get; set; }

    // --- adjudication outcome (null until adjudicated) ---
    public DateTime? AdjudicatedAtUtc { get; set; }
    public int? TotalAllowedCents { get; set; }
    public int? TotalPlanPaidCents { get; set; }
    public int? TotalMemberOwesCents { get; set; }

    /// <summary>Claim-level denial code, when the whole claim was denied.</summary>
    public DenialCode? DenialCode { get; set; }

    /// <summary>Lease fields: which worker owns this claim and until when (see ADR 0005).</summary>
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }

    /// <summary>Adjudication attempts — five failures dead-letter the claim.</summary>
    public int Attempts { get; set; }

    public List<ClaimLine> Lines { get; set; } = [];

    public const int FilingLimitDays = 365;
    public const int MaxAdjudicationAttempts = 5;

    public static string NextClaimNumber(int sequence, int year) => $"CLM-{year}-{sequence:D6}";
}

/// <summary>A single service line on a claim.</summary>
public sealed class ClaimLine
{
    public int Id { get; set; }

    public int ClaimId { get; set; }
    public Claim? Claim { get; set; }

    /// <summary>1-based line sequence on the claim form.</summary>
    public int Sequence { get; set; }

    /// <summary>CPT/HCPCS code billed on this line.</summary>
    public required string ProcedureCode { get; set; }

    public int ChargeCents { get; set; }

    // --- adjudication outcome (null until adjudicated) ---
    public int? AllowedCents { get; set; }
    public int? PlanPaidCents { get; set; }
    public int? MemberOwesCents { get; set; }
    public DenialCode? DenialCode { get; set; }
}
