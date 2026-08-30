namespace MediFlow.Domain.Claims.Adjudication;

/// <summary>
/// The claims adjudicator. Runs claim-level rules (timeliness, coverage,
/// duplicates); if none deny, prices each line through the benefit calculator.
/// Composed via DI so individual rules stay independently testable (see ADR 0002).
/// </summary>
public sealed class AdjudicationEngine(
    IEnumerable<IAdjudicationClaimRule> claimRules)
{
    private readonly IAdjudicationClaimRule[] _claimRules = [.. claimRules];

    public IReadOnlyList<IAdjudicationClaimRule> Rules => _claimRules;

    public AdjudicationResult Adjudicate(AdjudicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var rule in _claimRules)
        {
            if (rule.Evaluate(request) is { } denial)
            {
                return DenyAll(request, denial);
            }
        }

        var calculator = new BenefitCalculator(request.Plan, request.Accumulator);

        List<LineDecision> lines = [];
        foreach (var line in request.Claim.Lines.OrderBy(l => l.Sequence))
        {
            var fee = request.FeeSchedule.TryGetValue(line.ProcedureCode, out var f) ? f : null;
            lines.Add(calculator.PriceLine(
                line.Sequence,
                line.ProcedureCode,
                line.ChargeCents,
                fee?.AllowedCents ?? 0,
                fee is { IsCovered: true }));
        }

        return new AdjudicationResult
        {
            Status = ClaimStatus.Paid,
            Lines = lines,
            DeductibleAppliedCents = calculator.DeductibleAppliedTotal,
            NewDeductibleMetCents = calculator.DeductibleMet,
            NewOopMetCents = calculator.OopMet,
        };
    }

    /// <summary>Partial denials: a claim with any denied line is surfaced as Denied while paid lines still pay.</summary>
    private static AdjudicationResult DenyAll(AdjudicationRequest request, DenialCode denial) => new()
    {
        Status = ClaimStatus.Denied,
        ClaimDenialCode = denial,
        Lines =
        [
            .. request.Claim.Lines
                .OrderBy(l => l.Sequence)
                .Select(l => new LineDecision(l.Sequence, l.ProcedureCode, l.ChargeCents, 0, 0, 0, denial)),
        ],
    };
}
