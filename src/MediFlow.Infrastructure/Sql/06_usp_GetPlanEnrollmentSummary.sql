-- Plan performance: enrollment counts and premium volume by plan for a plan year.
CREATE OR ALTER PROCEDURE dbo.usp_GetPlanEnrollmentSummary
    @Year int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SELECT  p.Id, p.PlanCode, p.Name, p.Carrier, p.Type, p.ContractYear,
            p.MonthlyPremiumCents, p.IsFiveStar,
            COUNT(a.Id)                                        AS EnrollmentCount,
            SUM(CASE WHEN a.Status = 5 THEN 1 ELSE 0 END)      AS ActiveCount,
            SUM(CASE WHEN a.Status = 5 THEN p.MonthlyPremiumCents ELSE 0 END)
                                                                    AS MonthlyPremiumCents
    FROM    dbo.Plans AS p
            LEFT JOIN dbo.Enrollments AS a
                ON  a.PlanId = p.Id
                AND a.RequestedEffectiveDate >= CAST(CAST(@Year AS char(4)) + '-01-01' AS date)
                AND a.RequestedEffectiveDate <  DATEADD(year, 1, CAST(CAST(@Year AS char(4)) + '-01-01' AS date))
    WHERE   p.ContractYear = @Year
    GROUP BY p.Id, p.PlanCode, p.Name, p.Carrier, p.Type, p.ContractYear,
            p.MonthlyPremiumCents, p.IsFiveStar
    ORDER BY ActiveCount DESC, p.PlanCode;
END
GO
