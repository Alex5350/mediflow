# ADR 0006: Single-round-trip commit via table-valued parameter

- Status: Accepted
- Date: 2026-08-30

## Context

Committing one adjudication result means writing the claim header (status,
totals, denial code), an outcome for every claim line, an upsert of the
member's benefit accumulators, an "Adjudicated" audit row, and the completion
of the outbox message that triggered the work — well over twenty rows across
five tables for a 20-line claim. Doing this from C# with EF Core would mean one
transaction, several round trips, and the lease guard expressed as optimistic
client logic.

## Decision

One stored procedure call commits everything: `usp_RecordAdjudicationResult`
(src/MediFlow.Infrastructure/Sql/09_usp_RecordAdjudicationResult.sql). The line
outcomes travel as a table-valued parameter of type
`dbo.AdjudicationLineResultType` (defined in `01_Types.sql`: sequence, procedure
code, charge/allowed/plan-paid/member-owes cents, denial code). Inside a single
transaction the procedure:

1. Guards the lease — the claim must still be `Adjudicating` under the caller's
   `@LeaseToken`, otherwise `ROLLBACK` + `THROW 51002` (ADR 0005).
2. Updates the claim header and clears the lease.
3. Updates every line by joining `@LineResults` on sequence.
4. `MERGE`s the member's `BenefitAccumulators` row for the service-date year
   (adding deductible applied, setting OOP met), inserting when absent.
5. Inserts the audit entry with a `FOR JSON` detail payload.
6. Completes the pending `adjudicate-claim` outbox message.

`AdjudicationGateway.CommitAdjudicationAsync` builds a `DataTable` and issues
the call with raw ADO.NET rather than Dapper, because `DynamicParameters` does
not carry the table type name (`dbo.AdjudicationLineResultType`) in the package
versions targeted — a `SqlParameter` with `TypeName` set is required for TVPs.

## Why a stored procedure rather than an EF transaction

- **One atomic unit, instantly observable.** Other readers — the dashboard's
  queue and stats queries, the member 360 view — never see a half-committed
  claim (lines updated but header still `Adjudicating`, or an accumulator
  advanced with no audit row). The intermediate states simply do not exist
  outside the transaction.
- **The guard lives with the writes it protects.** The lease check and the five
  writes it authorizes sit in the same script; there is no way to add a write
  path that forgets the guard, and no client-side race window between checking
  the lease and writing.
- **One round trip.** The whole result, regardless of line count, crosses the
  network once.

## Consequences

- The commit's semantics live in SQL and are verified by the integration suite
  (`AdjudicationGatewayTests` commits a real claim and asserts the persisted
  header, lines, and totals; a wrong-token commit must throw).
- The TVP's column set and the domain's `LineDecision` must evolve together —
  an accepted trade, since both ship in the same repository and the procedures
  are embedded resources in the same assembly (ADR 0003).
- All money crossing the boundary is already integer cents (ADR 0004), so the
  TVP carries `int` columns with no conversion risk.
