# ADR 0005: Transactional outbox with SQL leasing

- Status: Accepted
- Date: 2026-08-30

## Context

A claim is accepted by the Claims API but adjudicated seconds later by a worker
process. The handoff must not lose claims, must not double-adjudicate them, and
must survive workers crashing mid-flight - with more than one worker replica
running (the default in `deploy/k8s/worker.yaml`). A broker would solve this,
but would add a runtime dependency to what is already a transactional SQL
Server system.

## Decision

The outbox pattern, implemented entirely in SQL Server.

- **Intake.** `ClaimIntakeService.SubmitClaimAsync` writes the claim header,
  lines, an `OutboxMessage` (`Type = "adjudicate-claim"`, payload `{claimId}`),
  and the "Submitted" audit entry in one EF Core transaction. A crash between
  "claim saved" and "work queued" is impossible - they are the same
  transaction.
- **Leasing.** The worker polls `usp_LeaseNextClaims` every 5 seconds (15 when
  idle). The procedure leases up to `@BatchSize` outbox rows (the worker uses
  10) with `READPAST, UPDLOCK` hints, so concurrent workers neither lease the
  same row nor block each other; it stamps `LeaseToken` and a 2-minute
  `LeasedUntilUtc`, increments `Attempts`, flips the claims `Received` to
  `Adjudicating` under the same token, and refuses the batch (`THROW 51001`)
  on an unparseable payload.
- **Commit guard.** `usp_RecordAdjudicationResult` accepts a result only from
  the current lease holder; a stale or wrong token throws `51002` (ADR 0006).
- **Failure.** On an unexpected exception the worker calls `FailLeaseAsync`:
  the claim returns to `Received`, the outbox lease clears, and
  `AvailableAtUtc` is pushed out by the retry delay (30 seconds today), so the
  message is not immediately re-leased. After 5 attempts the claim is marked
  `DeadLettered`, the message completed, and an audit row written.
- **Self-healing sweep.** `usp_LeaseNextClaims` opens by dead-lettering any
  claim whose pending outbox message has `Attempts >= 5` - the residue of a
  worker that crashed before it could call `FailLeaseAsync`.

## Crash scenarios covered

Crash after intake (message still pending; next poll leases it); crash after
lease before commit (lease lapses in 2 minutes, re-leased with `Attempts`
already advanced, bounding retries); crash during commit (the procedure's
transaction rolls back atomically); crash after commit (outbox already
completed, no re-lease); two workers racing (`READPAST`/`UPDLOCK` make
double-leasing impossible, and the commit-time token guard rejects any result
that slips through an expired lease).

## Consequences

- Exactly-once *processing intent* with at-least-once *delivery*: the engine
  may re-run on a claim after a crashed lease, but only one commit can win.
- No broker to operate; the cost is poll latency and two worker-side stored
  procedures that must stay in sync with the EF model.
- The `IX_Outbox_Pending` filtered index (`[CompletedAtUtc] IS NULL`) and
  `IX_Claims_Queue` (`[Status] IN (0, 1)`) keep the lease and queue scans off
  the growing completed/history rows.
- Dead-lettered claims surface on the dashboard's operations page and can be
  requeued via `POST /api/v1/claims/{id}/adjudicate`, which drops a new,
  immediately-available outbox message.
