-- Denial analysis: counts and dollars grouped by adjustment code for a plan year.
CREATE OR ALTER PROCEDURE dbo.usp_GetDenialRollup
    @Year int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SELECT  l.DenialCode,
            COUNT(DISTINCT c.Id)          AS ClaimCount,
            COUNT(*)                      AS LineCount,
            SUM(l.ChargeCents)            AS ChargedCents,
            SUM(l.ChargeCents - COALESCE(l.PlanPaidCents, 0)) AS UnpaidCents
    FROM    dbo.ClaimLines AS l
            INNER JOIN dbo.Claims AS c ON c.Id = l.ClaimId
    WHERE   l.DenialCode IS NOT NULL
      AND   YEAR(c.ServiceDate) = @Year
    GROUP BY l.DenialCode
    ORDER BY ClaimCount DESC;
END
GO
