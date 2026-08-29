namespace MediFlow.Contracts.Enrollment;

public sealed record SubmitEnrollmentRequest
{
    public required int MemberId { get; init; }
    public required int PlanId { get; init; }
    public DateOnly RequestedEffectiveDate { get; init; }
    public int SepReason { get; init; }
}

/// <summary>Validation outcome returned as 400 ValidationProblem by the API.</summary>
public sealed record EnrollmentValidationDto(bool IsValid, IReadOnlyList<EnrollmentViolationDto> Violations);

public sealed record EnrollmentViolationDto(int Code, string Message);

public sealed record EnrollmentDecisionRequest
{
    public required bool Approve { get; init; }
    public string? Note { get; init; }
}

public sealed record EnrollmentDto
{
    public required int Id { get; init; }
    public required string ApplicationNumber { get; init; }
    public required int MemberId { get; init; }
    public required string MemberName { get; init; }
    public required string Mbi { get; init; }
    public required int PlanId { get; init; }
    public required string PlanCode { get; init; }
    public required string PlanName { get; init; }
    public required int Status { get; init; }
    public required int SepReason { get; init; }
    public DateOnly RequestedEffectiveDate { get; init; }
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public string? DecisionNote { get; init; }
}
