namespace MediFlow.Contracts.Claims;

public sealed record SubmitClaimRequest
{
    public required int MemberId { get; init; }
    public required int PlanId { get; init; }
    public required string RenderingProviderNpi { get; init; }
    public required int Type { get; init; }
    public DateOnly ServiceDate { get; init; }
    public required IReadOnlyList<SubmitClaimLine> Lines { get; init; }
}

public sealed record SubmitClaimLine
{
    public required string ProcedureCode { get; init; }
    public int ChargeCents { get; init; }
}

public sealed record ClaimSubmissionResultDto(bool Accepted, int? ClaimId, string? ClaimNumber, IReadOnlyList<ClaimSubmissionViolationDto> Violations);

public sealed record ClaimSubmissionViolationDto(string Code, string Message);

public sealed record ClaimQueueItemDto
{
    public required int Id { get; init; }
    public required string ClaimNumber { get; init; }
    public required int MemberId { get; init; }
    public required string Mbi { get; init; }
    public required string LastName { get; init; }
    public required string FirstName { get; init; }
    public required int Type { get; init; }
    public DateOnly ServiceDate { get; init; }
    public int TotalChargeCents { get; init; }
    public required int Status { get; init; }
    public int? TotalPlanPaidCents { get; init; }
    public int? TotalMemberOwesCents { get; init; }
    public int? DenialCode { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
    public DateTime? AdjudicatedAtUtc { get; init; }
    public int Attempts { get; init; }
    public int TotalCount { get; init; }
}

public sealed record ClaimLineDto
{
    public int Sequence { get; init; }
    public required string ProcedureCode { get; init; }
    public int ChargeCents { get; init; }
    public int? AllowedCents { get; init; }
    public int? PlanPaidCents { get; init; }
    public int? MemberOwesCents { get; init; }
    public int? DenialCode { get; init; }
}

public sealed record ClaimDetailDto
{
    public required int Id { get; init; }
    public required string ClaimNumber { get; init; }
    public required int MemberId { get; init; }
    public required string MemberName { get; init; }
    public required string Mbi { get; init; }
    public required string PlanCode { get; init; }
    public required string PlanName { get; init; }
    public required string RenderingProviderNpi { get; init; }
    public required int Type { get; init; }
    public DateOnly ServiceDate { get; init; }
    public int TotalChargeCents { get; init; }
    public required int Status { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
    public DateTime? AdjudicatedAtUtc { get; init; }
    public int? TotalAllowedCents { get; init; }
    public int? TotalPlanPaidCents { get; init; }
    public int? TotalMemberOwesCents { get; init; }
    public int? DenialCode { get; init; }
    public required IReadOnlyList<ClaimLineDto> Lines { get; init; }
    public required IReadOnlyList<ClaimAuditRowDto> Audit { get; init; }
}

public sealed record ClaimAuditRowDto(string Action, string Actor, DateTime AtUtc, string? DetailJson);

/// <summary>Dry-run decision returned by the MCP preview tool and the API preview endpoint.</summary>
public sealed record AdjudicationPreviewDto
{
    public required string ClaimNumber { get; init; }
    public required string Status { get; init; }
    public string? ClaimDenialCode { get; init; }
    public required IReadOnlyList<AdjudicationPreviewLineDto> Lines { get; init; }
    public int TotalAllowedCents { get; init; }
    public int TotalPlanPaidCents { get; init; }
    public int TotalMemberOwesCents { get; init; }
    public int NewDeductibleMetCents { get; init; }
    public int NewOopMetCents { get; init; }
}

public sealed record AdjudicationPreviewLineDto
{
    public required int Sequence { get; init; }
    public required string ProcedureCode { get; init; }
    public int ChargeCents { get; init; }
    public int AllowedCents { get; init; }
    public int PlanPaidCents { get; init; }
    public int MemberOwesCents { get; init; }
    public string? DenialCode { get; init; }
}

public sealed record DenialRollupDto
{
    public required int DenialCode { get; init; }
    public int ClaimCount { get; init; }
    public int LineCount { get; init; }
    public long ChargedCents { get; init; }
    public long UnpaidCents { get; init; }
}
