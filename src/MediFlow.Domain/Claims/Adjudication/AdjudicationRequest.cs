namespace MediFlow.Domain.Claims.Adjudication;

using Accumulators;
using Enrollment;
using Fees;
using Members;
using Plans;

/// <summary>Prior claim fingerprint used for duplicate detection (CO-18).</summary>
/// <param name="RenderingProviderNpi">Provider on the prior claim.</param>
/// <param name="ServiceDate">Service date on the prior claim.</param>
/// <param name="ProcedureCode">One procedure billed on the prior claim.</param>
public readonly record struct PriorClaimFingerprint(string RenderingProviderNpi, DateOnly ServiceDate, string ProcedureCode);

/// <summary>Everything the adjudicator needs; assembled by the worker after leasing a claim.</summary>
public sealed record AdjudicationRequest(
    Claim Claim,
    Member Member,
    Plan Plan,
    EnrollmentApplication? CoverageEnrollment,
    IReadOnlyDictionary<string, ProcedureFee> FeeSchedule,
    BenefitAccumulator Accumulator,
    IReadOnlyList<PriorClaimFingerprint> PriorClaims)
{
    /// <summary>True when the member's enrollment covers the claim's service date.</summary>
    public bool HasCoverageOnServiceDate =>
        CoverageEnrollment is { Status: EnrollmentStatus.Active } &&
        CoverageEnrollment.RequestedEffectiveDate <= Claim.ServiceDate &&
        (CoverageEnrollment.CancelledEffectiveDate is null || Claim.ServiceDate <= CoverageEnrollment.CancelledEffectiveDate);
}
