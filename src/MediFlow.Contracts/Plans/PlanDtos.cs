namespace MediFlow.Contracts.Plans;

public sealed record PlanDto
{
    public required int Id { get; init; }
    public required string PlanCode { get; init; }
    public required string Name { get; init; }
    public required string Carrier { get; init; }
    public required int Type { get; init; }
    public int ContractYear { get; init; }
    public int MonthlyPremiumCents { get; init; }
    public int DeductibleCents { get; init; }
    public byte CoinsurancePercent { get; init; }
    public int OopMaxCents { get; init; }
    public bool IsFiveStar { get; init; }
}

public sealed record PlanEnrollmentSummaryDto
{
    public required int Id { get; init; }
    public required string PlanCode { get; init; }
    public required string Name { get; init; }
    public required string Carrier { get; init; }
    public required int Type { get; init; }
    public int ContractYear { get; init; }
    public int MonthlyPremiumCents { get; init; }
    public bool IsFiveStar { get; init; }
    public int EnrollmentCount { get; init; }
    public int ActiveCount { get; init; }
    public long MonthlyPremiumCentsTotal { get; init; }
}

/// <summary>Result shape of usp_GetDashboardStats.</summary>
public sealed record DashboardStatsDto
{
    public int ClaimsReceived { get; init; }
    public int ClaimsAdjudicating { get; init; }
    public int ClaimsOpen { get; init; }
    public int ClaimsDeadLettered { get; init; }
    public int ClaimsPaid30d { get; init; }
    public int ClaimsDenied30d { get; init; }
    public int EnrollmentsPending { get; init; }
    public int EnrollmentsActive { get; init; }
    public long YtdPlanPaidCents { get; init; }
    public long YtdMemberOwesCents { get; init; }
    public int OutboxDepth { get; init; }
}
