namespace MediFlow.Domain.Enrollment;

/// <summary>Lifecycle states of an enrollment application.</summary>
public enum EnrollmentStatus
{
    Draft = 0,
    Submitted = 1,
    PendingVerification = 2,
    Approved = 3,
    Denied = 4,
    Active = 5,
    Cancelled = 6,
}

/// <summary>
/// Special Enrollment Period reasons that permit enrollment outside the
/// Annual Enrollment Period (Oct 15 – Dec 7).
/// </summary>
public enum SepReason
{
    /// <summary>No SEP applies — the Annual/Initial Enrollment Period must cover the request.</summary>
    None = 0,

    /// <summary>Member moved out of the plan's service area.</summary>
    Moved = 1,

    /// <summary>Member lost other creditable coverage (e.g. employer plan ended).</summary>
    LostCreditableCoverage = 2,

    /// <summary>Member qualifies for Medicaid (dual eligible) — continuous SEP.</summary>
    DualEligible = 3,

    /// <summary>Member qualifies for Extra Help / Low-Income Subsidy — continuous SEP.</summary>
    LowIncomeSubsidy = 4,

    /// <summary>Switching into a CMS 5-star plan — allowed once per year, any month.</summary>
    FiveStar = 5,
}

/// <summary>A rule-violation code returned by <see cref="EnrollmentRules"/>.</summary>
public enum EnrollmentViolation
{
    None = 0,
    PlanNotOfferedForYear = 1,
    PartBNotEffective = 2,
    AlreadyEnrolledSameType = 3,
    OutsideEnrollmentWindow = 4,
    EffectiveDateNotFirstOfMonth = 5,
}
