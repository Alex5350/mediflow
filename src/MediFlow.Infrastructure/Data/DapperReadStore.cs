namespace MediFlow.Infrastructure.Data;

using Dapper;
using MediFlow.Contracts.Claims;
using MediFlow.Contracts.Members;
using MediFlow.Contracts.Plans;
using MediFlow.Domain.Claims;
using System.Data.Common;

/// <summary>
/// Stored-procedure-backed read paths. Every method targets one proc from
/// Infrastructure/Sql — the tuning story for these queries lives in
/// docs/query-tuning.md (ADR 0003).
/// </summary>
public interface IReadStore
{
    Task<PagedResult<MemberSearchResultDto>> SearchMembersAsync(string query, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<Member360Dto?> GetMember360Async(int memberId, CancellationToken ct = default);
    Task<PagedResult<ClaimQueueItemDto>> ClaimsQueueAsync(IReadOnlyCollection<ClaimStatus>? statuses, DateOnly? serviceFrom, DateOnly? serviceTo, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<DenialRollupDto>> DenialRollupAsync(int year, CancellationToken ct = default);
    Task<IReadOnlyList<PlanEnrollmentSummaryDto>> PlanEnrollmentSummaryAsync(int year, CancellationToken ct = default);
    Task<DashboardStatsDto> DashboardStatsAsync(CancellationToken ct = default);
}

public sealed class DapperReadStore(IDbConnectionFactory connectionFactory) : IReadStore
{
    public async Task<PagedResult<MemberSearchResultDto>> SearchMembersAsync(string query, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_SearchMembers",
            new { Query = NormalizeQuery(query), PageIndex = Math.Max(1, pageIndex), PageSize = Math.Clamp(pageSize, 1, 100) },
            cancellationToken: ct);
        var rows = (await connection.QueryAsync<MemberSearchResultDto>(command)).AsList();
        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        return new PagedResult<MemberSearchResultDto>(rows, total, pageIndex, pageSize);
    }

    public async Task<Member360Dto?> GetMember360Async(int memberId, CancellationToken ct = default)
    {
        await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_GetMember360", new { MemberId = memberId }, cancellationToken: ct);
        await using var multi = await connection.QueryMultipleAsync(command);

        var header = await multi.ReadFirstOrDefaultAsync<Member360HeaderDto>();
        if (header is null)
        {
            return null;
        }

        var enrollments = (await multi.ReadAsync<MemberEnrollmentHistoryDto>()).AsList();
        var claims = (await multi.ReadAsync<MemberClaimRowDto>()).AsList();
        return new Member360Dto(header, enrollments, claims);
    }

    public async Task<PagedResult<ClaimQueueItemDto>> ClaimsQueueAsync(IReadOnlyCollection<ClaimStatus>? statuses, DateOnly? serviceFrom, DateOnly? serviceTo, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_ClaimsQueue",
            new
            {
                StatusCsv = statuses is { Count: > 0 } ? string.Join(',', statuses.Select(s => (int)s)) : null,
                ServiceDateFrom = serviceFrom,
                ServiceDateTo = serviceTo,
                PageIndex = Math.Max(1, pageIndex),
                PageSize = Math.Clamp(pageSize, 1, 100),
            },
            cancellationToken: ct);
        var rows = (await connection.QueryAsync<ClaimQueueItemDto>(command)).AsList();
        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        return new PagedResult<ClaimQueueItemDto>(rows, total, pageIndex, pageSize);
    }

    public async Task<IReadOnlyList<DenialRollupDto>> DenialRollupAsync(int year, CancellationToken ct = default)
    {
        await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_GetDenialRollup", new { Year = year }, cancellationToken: ct);
        return (await connection.QueryAsync<DenialRollupDto>(command)).AsList();
    }

    public async Task<IReadOnlyList<PlanEnrollmentSummaryDto>> PlanEnrollmentSummaryAsync(int year, CancellationToken ct = default)
    {
        await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_GetPlanEnrollmentSummary", new { Year = year }, cancellationToken: ct);
        return (await connection.QueryAsync<PlanEnrollmentSummaryDto>(command)).AsList();
    }

    public async Task<DashboardStatsDto> DashboardStatsAsync(CancellationToken ct = default)
    {
        await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_GetDashboardStats", cancellationToken: ct);
        return await connection.QuerySingleAsync<DashboardStatsDto>(command);
    }

    /// <summary>Uppercases and strips MBI dashes so searches behave like the stored form.</summary>
    private static string NormalizeQuery(string query) =>
        query.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
