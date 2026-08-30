-- Adjudication work queue: status filter (CSV of ints), service-date range, paging.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimsQueue
    @StatusCsv        nvarchar(200) = NULL,
    @ServiceDateFrom  date = NULL,
    @ServiceDateTo    date = NULL,
    @PageIndex        int  = 1,
    @PageSize         int  = 25
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SELECT  c.Id, c.ClaimNumber, c.MemberId, m.Mbi, m.LastName, m.FirstName,
            c.Type, c.ServiceDate, c.TotalChargeCents, c.Status,
            c.TotalPlanPaidCents, c.TotalMemberOwesCents, c.DenialCode,
            c.ReceivedAtUtc, c.AdjudicatedAtUtc, c.Attempts,
            COUNT(*) OVER() AS TotalCount
    FROM    dbo.Claims AS c
            INNER JOIN dbo.Members AS m ON m.Id = c.MemberId
    WHERE   (@StatusCsv IS NULL OR c.Status IN (SELECT value FROM STRING_SPLIT(@StatusCsv, ',')))
      AND   (@ServiceDateFrom IS NULL OR c.ServiceDate >= @ServiceDateFrom)
      AND   (@ServiceDateTo IS NULL OR c.ServiceDate <= @ServiceDateTo)
    ORDER BY c.ReceivedAtUtc DESC, c.Id DESC
    OFFSET (@PageIndex - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
GO
