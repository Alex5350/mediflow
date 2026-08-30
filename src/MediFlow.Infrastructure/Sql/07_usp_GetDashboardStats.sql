-- Dashboard KPIs in a single round trip: queue depth, throughput, denial rate,
-- enrollment pipeline, dead letters and year-to-date dollars.
CREATE OR ALTER PROCEDURE dbo.usp_GetDashboardStats
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SELECT
        (SELECT COUNT(*) FROM dbo.Claims WHERE Status = 0)                          AS ClaimsReceived,
        (SELECT COUNT(*) FROM dbo.Claims WHERE Status = 1)                          AS ClaimsAdjudicating,
        (SELECT COUNT(*) FROM dbo.Claims WHERE Status IN (0, 1))                    AS ClaimsOpen,
        (SELECT COUNT(*) FROM dbo.Claims WHERE Status = 5)                          AS ClaimsDeadLettered,
        (SELECT COUNT(*) FROM dbo.Claims
            WHERE Status = 2 AND AdjudicatedAtUtc >= DATEADD(day, -30, SYSUTCDATETIME())) AS ClaimsPaid30d,
        (SELECT COUNT(*) FROM dbo.Claims
            WHERE Status = 3 AND AdjudicatedAtUtc >= DATEADD(day, -30, SYSUTCDATETIME())) AS ClaimsDenied30d,
        (SELECT COUNT(*) FROM dbo.Enrollments WHERE Status = 2)                     AS EnrollmentsPending,
        (SELECT COUNT(*) FROM dbo.Enrollments WHERE Status = 5)                     AS EnrollmentsActive,
        (SELECT COALESCE(SUM(COALESCE(TotalPlanPaidCents, 0)), 0)
            FROM dbo.Claims WHERE YEAR(ServiceDate) = YEAR(SYSUTCDATETIME()))       AS YtdPlanPaidCents,
        (SELECT COALESCE(SUM(COALESCE(TotalMemberOwesCents, 0)), 0)
            FROM dbo.Claims WHERE YEAR(ServiceDate) = YEAR(SYSUTCDATETIME()))       AS YtdMemberOwesCents,
        (SELECT COUNT(*) FROM dbo.Outbox WHERE CompletedAtUtc IS NULL)              AS OutboxDepth;
END
GO
