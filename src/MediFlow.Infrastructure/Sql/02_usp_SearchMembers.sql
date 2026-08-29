-- Staff member search: matches MBI (normalized, dashes stripped) or name prefix.
-- COUNT(*) OVER() gives the total row count for pager UIs without a second query.
CREATE OR ALTER PROCEDURE dbo.usp_SearchMembers
    @Query    nvarchar(64),
    @PageIndex int = 1,
    @PageSize  int = 25
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SELECT  m.Id,
            m.Mbi,
            m.FirstName,
            m.LastName,
            m.DateOfBirth,
            m.StateCode,
            m.PartAEffective,
            m.PartBEffective,
            COUNT(*) OVER() AS TotalCount
    FROM    dbo.Members AS m
    WHERE   m.Mbi LIKE @Query + N'%'
       OR   m.LastName LIKE @Query + N'%'
       OR   m.FirstName LIKE @Query + N'%'
    ORDER BY m.LastName, m.FirstName, m.Id
    OFFSET (@PageIndex - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
GO
