namespace MediFlow.Domain.Claims.Adjudication;

using Accumulators;
using Common;
using Plans;

/// <summary>
/// Line-level benefit math: fee allowance → deductible → coinsurance → OOP-max cap.
/// Pure and sequential across the claim's lines, advancing running accumulator
/// values so a multi-line claim consumes its deductible in line order.
/// All rounding is away-from-zero at half-cent boundaries (ADR 0004) so the
/// engine, the SQL commit and the tests agree to the cent.
/// </summary>
public sealed class BenefitCalculator
{
    private readonly Plan _plan;

    private int _deductibleMet;
    private int _oopMet;

    public BenefitCalculator(Plan plan, BenefitAccumulator accumulator)
    {
        _plan = plan;
        _deductibleMet = Math.Clamp(accumulator.DeductibleMetCents, 0, plan.DeductibleCents);
        _oopMet = Math.Clamp(accumulator.OopMetCents, 0, plan.OopMaxCents);
    }

    public int DeductibleAppliedTotal { get; private set; }
    public int DeductibleMet => _deductibleMet;
    public int OopMet => _oopMet;

    /// <summary>Adjudicates one line against the remaining benefit accumulators.</summary>
    public LineDecision PriceLine(int sequence, string procedureCode, int chargeCents, int allowedCents, bool covered)
    {
        if (!covered || allowedCents <= 0)
        {
            return new LineDecision(sequence, procedureCode, chargeCents, 0, 0, 0, DenialCode.NonCoveredService);
        }

        var allowed = Math.Min(chargeCents, allowedCents);

        // 1) Deductible: member pays first until the plan deductible is met.
        var deductibleRemaining = Math.Max(0, _plan.DeductibleCents - _deductibleMet);
        var deductibleApplied = Math.Min(allowed, deductibleRemaining);

        // 2) Coinsurance: member pays a percentage of what's left after the deductible.
        var afterDeductible = allowed - deductibleApplied;
        var coinsurance = Money.PercentOf(afterDeductible, _plan.CoinsurancePercent);

        var memberOwesBeforeCap = deductibleApplied + coinsurance;

        // 3) OOP max: once the member hits the annual cap the plan pays the excess.
        var oopRemaining = Math.Max(0, _plan.OopMaxCents - _oopMet);
        var memberOwes = Math.Min(memberOwesBeforeCap, oopRemaining);
        var planPays = allowed - memberOwes;

        // Advance running accumulators.
        _deductibleMet += deductibleApplied;
        DeductibleAppliedTotal += deductibleApplied;
        _oopMet += memberOwes;

        var adjustment = deductibleApplied > 0 ? DenialCode.Deductible : DenialCode.Coinsurance;
        return new LineDecision(sequence, procedureCode, chargeCents, allowed, planPays, memberOwes, adjustment);
    }
}
