namespace MediFlow.Contracts.Members;

/// <summary>One row of staff member-search results. TotalCount repeats per row
/// (SQL COUNT(*) OVER()) and is collapsed by the client-side pager.</summary>
public sealed record MemberSearchResultDto
{
    public required int Id { get; init; }
    public required string Mbi { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public required string StateCode { get; init; }
    public DateOnly? PartAEffective { get; init; }
    public DateOnly? PartBEffective { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>Paged result envelope used across list endpoints.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int PageIndex, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>The member 360 header — member plus currently-active coverage.</summary>
public sealed record Member360HeaderDto
{
    public required int Id { get; init; }
    public required string Mbi { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public required string StateCode { get; init; }
    public DateOnly? PartAEffective { get; init; }
    public DateOnly? PartBEffective { get; init; }
    public int? PlanId { get; init; }
    public string? PlanCode { get; init; }
    public string? PlanName { get; init; }
    public int? PlanType { get; init; }
    public int? EnrollmentId { get; init; }
    public string? ApplicationNumber { get; init; }
    public int? EnrollmentStatus { get; init; }
    public DateOnly? RequestedEffectiveDate { get; init; }
    public DateOnly? CancelledEffectiveDate { get; init; }
}

public sealed record MemberEnrollmentHistoryDto
{
    public required int Id { get; init; }
    public required string ApplicationNumber { get; init; }
    public required int Status { get; init; }
    public required int SepReason { get; init; }
    public DateOnly RequestedEffectiveDate { get; init; }
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public string? DecisionNote { get; init; }
    public required string PlanCode { get; init; }
    public required string PlanName { get; init; }
    public required int PlanType { get; init; }
}

public sealed record MemberClaimRowDto
{
    public required int Id { get; init; }
    public required string ClaimNumber { get; init; }
    public required int Type { get; init; }
    public DateOnly ServiceDate { get; init; }
    public int TotalChargeCents { get; init; }
    public required int Status { get; init; }
    public int? TotalAllowedCents { get; init; }
    public int? TotalPlanPaidCents { get; init; }
    public int? TotalMemberOwesCents { get; init; }
    public int? DenialCode { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
    public DateTime? AdjudicatedAtUtc { get; init; }
    public int TotalCount { get; init; }
    public int YtdPlanPaidCents { get; init; }
    public int YtdMemberOwesCents { get; init; }
}

/// <summary>All three result sets of usp_GetMember360.</summary>
public sealed record Member360Dto(Member360HeaderDto? Header, IReadOnlyList<MemberEnrollmentHistoryDto> Enrollments, IReadOnlyList<MemberClaimRowDto> Claims);
