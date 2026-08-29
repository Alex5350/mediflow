namespace MediFlow.Domain.Enrollment;

using Members;
using Plans;

/// <summary>An eligibility check that failed, with a staff-facing message.</summary>
/// <param name="Code">Machine-readable violation.</param>
/// <param name="Message">Human-readable explanation for the decision notice.</param>
public readonly record struct EnrollmentRuleViolation(EnrollmentViolation Code, string Message);

/// <summary>Outcome of validating an enrollment request.</summary>
public readonly record struct EnrollmentValidation(bool IsValid, IReadOnlyList<EnrollmentRuleViolation> Violations)
{
    public static readonly EnrollmentValidation Valid = new(true, []);

    public static EnrollmentValidation Invalid(params ReadOnlySpan<EnrollmentRuleViolation> violations) =>
        new(false, violations.ToArray());
}

/// <summary>
/// Medicare enrollment eligibility rules: enrollment windows (AEP/SEP/ICEP),
/// entitlement checks and dual-coverage protection. Pure and deterministic so the
/// API, the MCP server and unit tests evaluate identical inputs identically. The
/// current date is always passed in by the caller, keeping boundaries testable.
/// </summary>
public static class EnrollmentRules
{
    /// <summary>Annual Enrollment Period — Oct 15 through Dec 7.</summary>
    public static readonly (int Month, int Day) AepStart = (10, 15);
    public static readonly (int Month, int Day) AepEnd = (12, 7);

    /// <summary>Months of entitlement covered by the Initial Enrollment Period around Part B start.</summary>
    public const int IcepMonthsAroundEntitlement = 3;

    public static EnrollmentValidation Validate(
        Member member,
        Plan plan,
        DateOnly requestedEffectiveDate,
        SepReason sepReason,
        IReadOnlyList<EnrollmentApplication> memberActiveEnrollments,
        DateOnly asOfUtc,
        DateTime submittedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(memberActiveEnrollments);

        List<EnrollmentRuleViolation> violations = [];
        var partBEffective = member.PartBEffective;

        if (plan.ContractYear != requestedEffectiveDate.Year)
        {
            violations.Add(new EnrollmentRuleViolation(
                EnrollmentViolation.PlanNotOfferedForYear,
                $"Plan {plan.PlanCode} is a {plan.ContractYear} product and cannot take a {requestedEffectiveDate.Year} effective date."));
        }

        if (partBEffective is not { } partB || partB > requestedEffectiveDate)
        {
            violations.Add(new EnrollmentRuleViolation(
                EnrollmentViolation.PartBNotEffective,
                "Member must be entitled to Medicare Part B on or before the requested effective date."));
        }

        var hasActiveSameType = memberActiveEnrollments.Any(a =>
            a.Plan is not null &&
            a.Plan.Type == plan.Type &&
            a.Status == EnrollmentStatus.Active &&
            (a.CancelledEffectiveDate is null || a.CancelledEffectiveDate >= requestedEffectiveDate));

        if (hasActiveSameType && sepReason != SepReason.FiveStar)
        {
            violations.Add(new EnrollmentRuleViolation(
                EnrollmentViolation.AlreadyEnrolledSameType,
                $"Member already has an active {plan.Type} enrollment; only a 5-star switch may replace it."));
        }

        if (requestedEffectiveDate.Day != 1)
        {
            violations.Add(new EnrollmentRuleViolation(
                EnrollmentViolation.EffectiveDateNotFirstOfMonth,
                "Coverage effective dates must be the first day of a month."));
        }

        if (!IsEffectiveDateAllowed(requestedEffectiveDate, sepReason, plan.IsFiveStar, partBEffective, asOfUtc, submittedAtUtc))
        {
            violations.Add(new EnrollmentRuleViolation(
                EnrollmentViolation.OutsideEnrollmentWindow,
                "Requested effective date is not reachable through AEP, ICEP, a qualifying SEP, or a 5-star switch."));
        }

        return violations.Count == 0
            ? EnrollmentValidation.Valid
            : new EnrollmentValidation(false, violations);
    }

    /// <summary>
    /// An effective date is reachable when any of: AEP (Oct 15–Dec 7 → Jan 1),
    /// ICEP (the 3 months either side of the member's Part B start), a qualifying
    /// SEP (first of the month following submission), or a 5-star switch any month.
    /// </summary>
    private static bool IsEffectiveDateAllowed(
        DateOnly requestedEffectiveDate,
        SepReason sepReason,
        bool isFiveStarPlan,
        DateOnly? partB,
        DateOnly asOfUtc,
        DateTime submittedAtUtc)
    {
        if (isFiveStarPlan && sepReason == SepReason.FiveStar)
        {
            return true;
        }

        if (partB is { } entitlement)
        {
            var icepStart = entitlement.AddMonths(-IcepMonthsAroundEntitlement);
            var icepEnd = entitlement.AddMonths(IcepMonthsAroundEntitlement);
            if (requestedEffectiveDate >= FirstOfMonth(icepStart) && requestedEffectiveDate <= FirstOfMonth(icepEnd))
            {
                return true;
            }
        }

        if (IsWithinAep(asOfUtc, out var aepEffectiveYear) &&
            requestedEffectiveDate == new DateOnly(aepEffectiveYear, 1, 1))
        {
            return true;
        }

        if (sepReason != SepReason.None)
        {
            // SEP effective dates: first of the month following the submission month.
            var nextMonthFirst = new DateOnly(submittedAtUtc.Year, submittedAtUtc.Month, 1).AddMonths(1);
            if (requestedEffectiveDate == nextMonthFirst)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True during the Annual Enrollment Period; <paramref name="effectiveYear"/> is the resulting coverage year (Jan 1).</summary>
    public static bool IsWithinAep(DateOnly date, out int effectiveYear)
    {
        var start = new DateOnly(date.Year, AepStart.Month, AepStart.Day);
        var end = new DateOnly(date.Year, AepEnd.Month, AepEnd.Day);
        if (date >= start && date <= end)
        {
            effectiveYear = date.Year + 1;
            return true;
        }

        effectiveYear = 0;
        return false;
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
