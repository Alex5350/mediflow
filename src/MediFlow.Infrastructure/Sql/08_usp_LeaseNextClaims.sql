-- Worker leasing: atomically claims up to @BatchSize pending outbox messages,
-- stamps the corresponding claims Adjudicating, and hands the claim ids back.
-- READPAST + UPDLOCK mean concurrent workers never lease (or block on) the same rows.
CREATE OR ALTER PROCEDURE dbo.usp_LeaseNextClaims
    @BatchSize    int,
    @LeaseToken   uniqueidentifier,
    @LeaseMinutes int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    DECLARE @swept TABLE (ClaimId int NOT NULL PRIMARY KEY);

    -- Self-healing sweep: outbox messages that exhausted their attempts (crashed
    -- workers never call FailLease) dead-letter their claims right here.
    UPDATE c
    SET    c.Status = 5 /* DeadLettered */, c.LeaseToken = NULL, c.LeaseExpiresUtc = NULL
    OUTPUT inserted.Id INTO @swept (ClaimId)
    FROM   dbo.Claims AS c
           INNER JOIN dbo.Outbox AS o
               ON  o.Type = N'adjudicate-claim'
               AND o.CompletedAtUtc IS NULL
               AND o.Attempts >= 5
               AND TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int) = c.Id
    WHERE  c.Status IN (0, 1);

    UPDATE o
    SET    o.CompletedAtUtc = SYSUTCDATETIME(),
           o.LastError = N'exhausted retry attempts'
    FROM   dbo.Outbox AS o
           INNER JOIN @swept AS s ON s.ClaimId = TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int)
    WHERE  o.Type = N'adjudicate-claim' AND o.CompletedAtUtc IS NULL;

    INSERT dbo.AuditEntries (EntityType, EntityKey, Action, DetailJson, Actor, AtUtc)
    SELECT N'Claim', c.ClaimNumber, N'DeadLettered', N'{"reason":"exhausted retry attempts"}', N'worker', SYSUTCDATETIME()
    FROM   dbo.Claims AS c
           INNER JOIN @swept AS s ON s.ClaimId = c.Id;

    DECLARE @leased TABLE (OutboxId bigint NOT NULL PRIMARY KEY, ClaimId int NOT NULL);

    ;WITH candidates AS (
        -- Columns updated through the CTE must appear in its projection.
        SELECT  TOP (@BatchSize)
                o.Id,
                o.LeaseToken,
                o.LeasedUntilUtc,
                o.Attempts,
                TRY_CAST(JSON_VALUE(o.PayloadJson, N'$.claimId') AS int) AS ClaimId
        FROM    dbo.Outbox AS o WITH (READPAST, UPDLOCK)
        WHERE   o.Type = N'adjudicate-claim'
          AND   o.CompletedAtUtc IS NULL
          AND   o.AvailableAtUtc <= SYSUTCDATETIME()
          AND   (o.LeasedUntilUtc IS NULL OR o.LeasedUntilUtc <= SYSUTCDATETIME())
          AND   o.Attempts < 5
        ORDER BY o.Id
    )
    UPDATE candidates
    SET    LeaseToken    = @LeaseToken,
           LeasedUntilUtc = DATEADD(minute, @LeaseMinutes, SYSUTCDATETIME()),
           Attempts      = candidates.Attempts + 1
    OUTPUT inserted.Id, inserted.ClaimId INTO @leased (OutboxId, ClaimId);

    IF EXISTS (SELECT 1 FROM @leased WHERE ClaimId IS NULL)
    BEGIN
        ROLLBACK TRAN;
        THROW 51001, 'Outbox rows with unparseable claimId payload — refusing to lease batch', 1;
    END

    UPDATE c
    SET    c.Status         = 1 /* Adjudicating */,
           c.LeaseToken     = @LeaseToken,
           c.LeaseExpiresUtc = DATEADD(minute, @LeaseMinutes, SYSUTCDATETIME())
    FROM    dbo.Claims AS c
            INNER JOIN @leased AS l ON l.ClaimId = c.Id
    WHERE   c.Status = 0 /* Received */;

    COMMIT TRAN;

    SELECT ClaimId FROM @leased ORDER BY OutboxId;
END
GO
