-- Atomic adjudication commit: one call writes the claim decision, every line,
-- the member's accumulators, the audit trail and completes the outbox message —
-- or throws and rolls back if the caller does not hold a valid lease (ADR 0005/0006).
CREATE OR ALTER PROCEDURE dbo.usp_RecordAdjudicationResult
    @ClaimId             int,
    @LeaseToken          uniqueidentifier,
    @Status              int,
    @ClaimDenialCode     int = NULL,
    @TotalAllowedCents   int,
    @TotalPlanPaidCents  int,
    @TotalMemberOwesCents int,
    @DeductibleAppliedCents int,
    @NewDeductibleMetCents  int,
    @NewOopMetCents      int,
    @LineResults         dbo.AdjudicationLineResultType READONLY,
    @Actor               nvarchar(64) = N'worker'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    -- Lease guard: the caller must still own the claim.
    IF NOT EXISTS (
        SELECT 1 FROM dbo.Claims
        WHERE   Id = @ClaimId
          AND   LeaseToken = @LeaseToken
          AND   Status = 1 /* Adjudicating */)
    BEGIN
        ROLLBACK TRAN;
        THROW 51002, 'Lease expired or not held — adjudication result rejected', 1;
    END

    DECLARE @claimNumber nvarchar(20), @memberId int, @benefitYear int, @nowUtc datetime2 = SYSUTCDATETIME();
    SELECT @claimNumber = ClaimNumber, @memberId = MemberId,
           @benefitYear = YEAR(ServiceDate)
    FROM dbo.Claims WHERE Id = @ClaimId;

    -- 1) Claim header outcome
    UPDATE dbo.Claims
    SET    Status = @Status,
           AdjudicatedAtUtc = @nowUtc,
           TotalAllowedCents = @TotalAllowedCents,
           TotalPlanPaidCents = @TotalPlanPaidCents,
           TotalMemberOwesCents = @TotalMemberOwesCents,
           DenialCode = @ClaimDenialCode,
           LeaseToken = NULL,
           LeaseExpiresUtc = NULL
    WHERE  Id = @ClaimId;

    -- 2) Line outcomes
    UPDATE l
    SET    l.AllowedCents   = lr.AllowedCents,
           l.PlanPaidCents  = lr.PlanPaidCents,
           l.MemberOwesCents = lr.MemberOwesCents,
           l.DenialCode     = lr.DenialCode
    FROM   dbo.ClaimLines AS l
           INNER JOIN @LineResults AS lr ON lr.Sequence = l.Sequence
    WHERE  l.ClaimId = @ClaimId;

    -- 3) Accumulator upsert (only when the claim actually priced benefits)
    IF @DeductibleAppliedCents > 0 OR @NewOopMetCents > 0
    BEGIN
        MERGE dbo.BenefitAccumulators AS target
        USING (SELECT @memberId AS MemberId, @benefitYear AS BenefitYear) AS src
        ON target.MemberId = src.MemberId AND target.BenefitYear = src.BenefitYear
        WHEN MATCHED THEN
            UPDATE SET target.DeductibleMetCents = target.DeductibleMetCents + @DeductibleAppliedCents,
                       target.OopMetCents = @NewOopMetCents
        WHEN NOT MATCHED THEN
            INSERT (MemberId, BenefitYear, DeductibleMetCents, OopMetCents)
            VALUES (@memberId, @benefitYear, @DeductibleAppliedCents, @NewOopMetCents);
    END

    -- 4) Audit trail
    INSERT dbo.AuditEntries (EntityType, EntityKey, Action, DetailJson, Actor, AtUtc)
    VALUES (N'Claim', @claimNumber, N'Adjudicated',
            (SELECT @Status AS Status, @ClaimDenialCode AS DenialCode,
                    @TotalPlanPaidCents AS PlanPaidCents,
                    @TotalMemberOwesCents AS MemberOwesCents
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            @Actor, @nowUtc);

    -- 5) Outbox completion for the claim's pending messages
    UPDATE o
    SET    o.CompletedAtUtc = @nowUtc
    FROM   dbo.Outbox AS o
    WHERE  o.Type = N'adjudicate-claim'
      AND  o.CompletedAtUtc IS NULL
      AND  TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int) = @ClaimId;

    COMMIT TRAN;
END
GO
