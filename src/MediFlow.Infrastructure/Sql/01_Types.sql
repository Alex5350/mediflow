-- Table type for passing adjudicated line results into usp_RecordAdjudicationResult
-- in a single round trip (see ADR 0006).
IF TYPE_ID(N'dbo.AdjudicationLineResultType') IS NULL
BEGIN
    CREATE TYPE dbo.AdjudicationLineResultType AS TABLE
    (
        Sequence      int          NOT NULL,
        ProcedureCode nvarchar(5)  NOT NULL,
        ChargeCents   int          NOT NULL,
        AllowedCents  int          NOT NULL,
        PlanPaidCents int          NOT NULL,
        MemberOwesCents int        NOT NULL,
        DenialCode    int          NULL
    );
END
GO
