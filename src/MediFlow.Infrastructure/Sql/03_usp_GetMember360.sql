-- Member 360: header + enrollment history + claims, three result sets in one call.
CREATE OR ALTER PROCEDURE dbo.usp_GetMember360
    @MemberId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 1) Header: member, active plan and application
    SELECT  m.Id, m.Mbi, m.FirstName, m.LastName, m.DateOfBirth, m.StateCode,
            m.PartAEffective, m.PartBEffective,
            p.Id AS PlanId, p.PlanCode, p.Name AS PlanName, p.Type AS PlanType,
            a.Id AS EnrollmentId, a.ApplicationNumber, a.Status AS EnrollmentStatus,
            a.RequestedEffectiveDate, a.CancelledEffectiveDate
    FROM    dbo.Members AS m
            LEFT JOIN dbo.Enrollments AS a
                ON a.MemberId = m.Id
               AND a.Status = 5 /* Active */
               AND (a.CancelledEffectiveDate IS NULL OR a.CancelledEffectiveDate >= CAST(SYSUTCDATETIME() AS date))
            LEFT JOIN dbo.Plans AS p ON p.Id = a.PlanId
    WHERE   m.Id = @MemberId;

    -- 2) Enrollment history
    SELECT  a.Id, a.ApplicationNumber, a.Status, a.SepReason,
            a.RequestedEffectiveDate, a.SubmittedAtUtc, a.DecidedAtUtc,
            a.DecisionNote, p.PlanCode, p.Name AS PlanName, p.Type AS PlanType
    FROM    dbo.Enrollments AS a
            INNER JOIN dbo.Plans AS p ON p.Id = a.PlanId
    WHERE   a.MemberId = @MemberId
    ORDER BY a.SubmittedAtUtc DESC;

    -- 3) Claims with year-to-date rollup
    SELECT  c.Id, c.ClaimNumber, c.Type, c.ServiceDate, c.TotalChargeCents,
            c.Status, c.TotalAllowedCents, c.TotalPlanPaidCents, c.TotalMemberOwesCents,
            c.DenialCode, c.ReceivedAtUtc, c.AdjudicatedAtUtc,
            COUNT(*) OVER() AS TotalCount,
            SUM(COALESCE(c.TotalPlanPaidCents, 0)) OVER() AS YtdPlanPaidCents,
            SUM(COALESCE(c.TotalMemberOwesCents, 0)) OVER() AS YtdMemberOwesCents
    FROM    dbo.Claims AS c
    WHERE   c.MemberId = @MemberId
    ORDER BY c.ServiceDate DESC
    OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY;
END
GO
