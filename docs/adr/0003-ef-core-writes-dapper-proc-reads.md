# ADR 0003: EF Core for writes, stored procedures + Dapper for hot reads

- Status: Accepted
- Date: 2026-08-30

## Context

MediFlow's data access has two shapes. The write side is OLTP: enrollments,
claims, audit rows — small transactions against a rich model that needs
migrations, unique indexes, and retry-on-failure. The read side is
projection-shaped: member search with paging totals, the claims work queue,
member 360 (three result sets in one call), denial and plan rollups, and
dashboard KPIs. Those queries join across aggregates, use window functions
(`COUNT(*) OVER()` for pager totals, `SUM(...) OVER()` for YTD figures), and
return staff-facing shapes that match no single entity.

## Decision

Both live in `src/MediFlow.Infrastructure`, behind two clearly separated
surfaces:

- **EF Core writes.** `MediFlowDbContext` owns the model, the unique/filtered
  indexes (unique MBI, unique `ClaimNumber`, `IX_Claims_Queue` filtered on
  `[Status] IN (0, 1)`, `IX_Outbox_Pending` filtered on
  `[CompletedAtUtc] IS NULL`), and all migrations. Writes go through services
  such as `EnrollmentService` and `ClaimIntakeService`, with
  `EnableRetryOnFailure` and an explicit execution strategy around user
  transactions.
- **Stored procedures + Dapper reads.** `DapperReadStore` implements `IReadStore`;
  every method is a single call to one procedure in `src/MediFlow.Infrastructure/Sql`
  (`usp_SearchMembers`, `usp_GetMember360`, `usp_ClaimsQueue`,
  `usp_GetDenialRollup`, `usp_GetPlanEnrollmentSummary`, `usp_GetDashboardStats`,
  plus the worker's `usp_LeaseNextClaims` / `usp_RecordAdjudicationResult`, see
  ADRs 0005 and 0006). The scripts are embedded resources applied in filename
  order by `SqlScriptRunner` on every boot, after migrations; every script is
  idempotent (`CREATE OR ALTER`, `IF TYPE_ID ... IS NULL`), so re-running is
  safe. A `DateOnlyTypeHandler` bridges SQL `date` to `DateOnly` for Dapper.

EF Core is appropriate where the model is the point: schema evolution, constraint
expression, and transactional writes of whole aggregates. Stored procedures are
appropriate where the query is the point: one round trip, window functions,
multi-result-set reads, and a tuning surface that can be adjusted without
recompiling C#. Keeping both in one project is deliberate — they share the
connection string, the `IDbConnectionFactory`, and one deployment story (procs
ship inside the assembly that calls them, so an app version and its procedures
can never skew), and the read DTOs both surfaces serve live in
`src/MediFlow.Contracts`.

## Consequences

- The DbContext stays free of read-shape complexity; hot queries never generate
  unpredictable LINQ SQL.
- Procedure changes are code-reviewed in the repository and covered by the
  integration suite (`StoredProcTests` verifies migrations plus all eight
  procedures and exercises each read path), at the cost of maintaining SQL
  alongside C# and contributors needing both skills.
- New OLTP features start in EF; a read path graduates to a procedure when it
  acquires paging, window functions, or multiple result sets.
