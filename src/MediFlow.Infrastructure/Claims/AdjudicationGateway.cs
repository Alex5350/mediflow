namespace MediFlow.Infrastructure.Claims;

using Dapper;
using Data;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Claims.Adjudication;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

/// <summary>
/// The worker's queue surface, backed by usp_LeaseNextClaims / usp_RecordAdjudicationResult.
/// Leasing is atomic in SQL, so any number of worker replicas can drain safely (ADR 0005).
/// </summary>
public interface IAdjudicationGateway
{
    Task<IReadOnlyList<int>> LeaseNextClaimsAsync(int batchSize, TimeSpan leaseDuration, Guid leaseToken, CancellationToken ct = default);
    Task CommitAdjudicationAsync(int claimId, Guid leaseToken, AdjudicationResult result, string actor = "worker", CancellationToken ct = default);
    Task FailLeaseAsync(int claimId, Guid leaseToken, string errorMessage, TimeSpan retryBackoff, CancellationToken ct = default);
}

public sealed class AdjudicationGateway(IDbConnectionFactory connectionFactory) : IAdjudicationGateway
{
    public async Task<IReadOnlyList<int>> LeaseNextClaimsAsync(int batchSize, TimeSpan leaseDuration, Guid leaseToken, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition("dbo.usp_LeaseNextClaims",
            new { BatchSize = Math.Clamp(batchSize, 1, 50), LeaseToken = leaseToken, LeaseMinutes = (int)Math.Ceiling(leaseDuration.TotalMinutes) },
            cancellationToken: ct);
        var claimIds = await connection.QueryAsync<int>(command);
        return claimIds.AsList();
    }

    public async Task CommitAdjudicationAsync(int claimId, Guid leaseToken, AdjudicationResult result, string actor = "worker", CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);

        var lineTable = new DataTable();
        lineTable.Columns.Add("Sequence", typeof(int));
        lineTable.Columns.Add("ProcedureCode", typeof(string));
        lineTable.Columns.Add("ChargeCents", typeof(int));
        lineTable.Columns.Add("AllowedCents", typeof(int));
        lineTable.Columns.Add("PlanPaidCents", typeof(int));
        lineTable.Columns.Add("MemberOwesCents", typeof(int));
        lineTable.Columns.Add("DenialCode", typeof(int)).AllowDBNull = true;
        foreach (var line in result.Lines)
        {
            lineTable.Rows.Add(
                line.Sequence,
                line.ProcedureCode,
                line.ChargeCents,
                line.AllowedCents,
                line.PlanPaidCents,
                line.MemberOwesCents,
                (object?)line.DenialCode ?? DBNull.Value);
        }

        // Raw ADO for the TVP round trip — Dapper's DynamicParameters does not carry
        // table type names in the versions we target (see ADR 0006).
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.usp_RecordAdjudicationResult";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 30;
        command.Parameters.AddRange(new SqlParameter[]
        {
            new SqlParameter("@ClaimId", claimId),
            new SqlParameter("@LeaseToken", leaseToken),
            new SqlParameter("@Status", (int)result.Status),
            new SqlParameter("@ClaimDenialCode", (object?)result.ClaimDenialCode ?? DBNull.Value),
            new SqlParameter("@TotalAllowedCents", result.TotalAllowedCents),
            new SqlParameter("@TotalPlanPaidCents", result.TotalPlanPaidCents),
            new SqlParameter("@TotalMemberOwesCents", result.TotalMemberOwesCents),
            new SqlParameter("@DeductibleAppliedCents", result.DeductibleAppliedCents),
            new SqlParameter("@NewDeductibleMetCents", result.NewDeductibleMetCents),
            new SqlParameter("@NewOopMetCents", result.NewOopMetCents),
            new SqlParameter("@LineResults", lineTable) { TypeName = "dbo.AdjudicationLineResultType" },
            new SqlParameter("@Actor", actor),
        });
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task FailLeaseAsync(int claimId, Guid leaseToken, string errorMessage, TimeSpan retryBackoff, CancellationToken ct = default)
    {
        // Releasing a lease is best-effort recovery, not a transactional decision:
        // the stored row's lease simply lapses and the claim becomes leasable again.
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        await connection.ExecuteAsync("""
            SET XACT_ABORT ON;
            BEGIN TRAN;

            DECLARE @attempts int, @maxAttempts int = 5;
            SELECT @attempts = o.Attempts
            FROM dbo.Outbox AS o WITH (UPDLOCK, HOLDLOCK)
            WHERE o.Type = N'adjudicate-claim'
              AND o.CompletedAtUtc IS NULL
              AND TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int) = @ClaimId
              AND EXISTS (SELECT 1 FROM dbo.Claims WHERE Id = @ClaimId AND LeaseToken = @LeaseToken);

            IF @attempts IS NOT NULL
            BEGIN
                IF @attempts >= @maxAttempts
                BEGIN
                    UPDATE dbo.Claims
                    SET Status = 5 /* DeadLettered */, LeaseToken = NULL, LeaseExpiresUtc = NULL
                    WHERE Id = @ClaimId;

                    UPDATE o SET o.CompletedAtUtc = SYSUTCDATETIME(), o.LastError = @Error
                    FROM dbo.Outbox o
                    WHERE o.Type = N'adjudicate-claim' AND o.CompletedAtUtc IS NULL
                      AND TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int) = @ClaimId;

                    INSERT dbo.AuditEntries (EntityType, EntityKey, Action, DetailJson, Actor, AtUtc)
                    SELECT N'Claim', c.ClaimNumber, N'DeadLettered', @Error, N'worker', SYSUTCDATETIME()
                    FROM dbo.Claims c WHERE c.Id = @ClaimId;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Claims
                    SET Status = 0 /* Received */, LeaseToken = NULL, LeaseExpiresUtc = NULL
                    WHERE Id = @ClaimId;

                    UPDATE o
                    SET o.LeaseToken = NULL, o.LeasedUntilUtc = NULL,
                        o.AvailableAtUtc = DATEADD(second, @RetrySeconds, SYSUTCDATETIME()),
                        o.LastError = @Error
                    FROM dbo.Outbox o
                    WHERE o.Type = N'adjudicate-claim' AND o.CompletedAtUtc IS NULL
                      AND TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int) = @ClaimId;
                END
            END

            COMMIT TRAN;
            """,
            new { ClaimId = claimId, LeaseToken = leaseToken, Error = Truncate(errorMessage, 512), RetrySeconds = (int)retryBackoff.TotalSeconds },
            commandTimeout: 30);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
