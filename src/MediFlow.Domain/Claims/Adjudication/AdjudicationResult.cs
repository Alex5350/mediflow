namespace MediFlow.Domain.Claims.Adjudication;

/// <summary>Per-line adjudication outcome written back to <c>ClaimLines</c>.</summary>
/// <param name="Sequence">Matching line sequence from the submitted claim.</param>
/// <param name="ProcedureCode">CPT/HCPCS code as billed.</param>
/// <param name="ChargeCents">Billed amount.</param>
/// <param name="AllowedCents">Fee-schedule allowed amount (0 for denied lines).</param>
/// <param name="PlanPaidCents">What the plan pays.</param>
/// <param name="MemberOwesCents">What the member owes (deductible/coinsurance/copay).</param>
/// <param name="DenialCode">Adjustment code — CO-* denials or the PR-* component that explains member responsibility.</param>
public sealed record LineDecision(
    int Sequence,
    string ProcedureCode,
    int ChargeCents,
    int AllowedCents,
    int PlanPaidCents,
    int MemberOwesCents,
    DenialCode? DenialCode = null);

/// <summary>The complete adjudication decision for one claim.</summary>
public sealed record AdjudicationResult
{
    public required ClaimStatus Status { get; init; }

    public required IReadOnlyList<LineDecision> Lines { get; init; }

    /// <summary>Claim-level denial code when <see cref="Status"/> is Denied.</summary>
    public DenialCode? ClaimDenialCode { get; init; }

    public int TotalAllowedCents => Lines.Sum(l => l.AllowedCents);
    public int TotalPlanPaidCents => Lines.Sum(l => l.PlanPaidCents);
    public int TotalMemberOwesCents => Lines.Sum(l => l.MemberOwesCents);

    // --- accumulator deltas the worker commits transactionally with the decision ---
    public int DeductibleAppliedCents { get; init; }
    public int NewDeductibleMetCents { get; init; }
    public int NewOopMetCents { get; init; }
}
