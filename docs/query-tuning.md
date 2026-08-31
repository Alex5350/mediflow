# Query tuning - the claims work queue

Every hot read path in MediFlow is a stored procedure (ADR 0003). This note shows
the index reasoning behind the two most contended ones, with real `STATISTICS IO`
output captured against the seeded demo database (160 members, 510 claims on the
day this was measured - ratios are what matter, not the absolute numbers).

Reproduce any of it against a running stack:

```bash
sqlcmd -S localhost,1433 -U sa -P 'MediFlow!Dev1' -d MediFlow \
  -Q "SET STATISTICS IO ON; EXEC dbo.usp_ClaimsQueue @StatusCsv='0,1'"
```

## 1. The queue scan: filtered index vs. clustered scan

The work queue only ever asks for claims in `Received` (0) or `Adjudicating` (1) -
roughly 6% of rows in a steady-state database, because adjudicated history
dominates. That is the textbook case for a **filtered index** (declared in
`MediFlowDbContext.OnModelCreating`):

```csharp
e.HasIndex(c => new { c.Status, c.ReceivedAtUtc })
    .HasFilter("[Status] IN (0, 1)")
    .HasDatabaseName("IX_Claims_Queue");
```

Measured on the queue predicate (`Status IN (0,1)`, ordered by `ReceivedAtUtc`):

| plan | logical reads | notes |
|---|---|---|
| seek on `IX_Claims_Queue` (filtered) | **4** | reads only the open-claim pages |
| clustered index scan (no secondary index) | **23** | reads every page of the table |

```
-- seek (as shipped)
Table 'Claims'. Scan count 2, logical reads 4, physical reads 0, ...

-- clustered scan (pre-index plan shape, forced with INDEX(1) to simulate
-- the table having no covering index for this predicate)
Table 'Claims'. Scan count 1, logical reads 23, physical reads 0, ...
```

At 510 claims the gap is ~6×. It grows linearly with table size while the
filtered index stays proportional to *open* claims - the queue stays O(work in
flight), not O(history), which is the property the worker's 5-second poll loop
depends on.

The pending-outbox scan (`usp_LeaseNextClaims`) gets the same treatment:
`IX_Outbox_Pending` is filtered on `[CompletedAtUtc] IS NULL`, so the leasing CTE
never touches completed messages no matter how much history accumulates.

## 2. Duplicate detection: seek, not scan

The adjudicator's CO-18 duplicate rule needs every procedure line a provider
billed a member on a given date. `IX_Claims (MemberId, ServiceDate)` turns that
into a two-seek join:

```
Table 'ClaimLines'. Scan count 1, logical reads 2
Table 'Claims'.     Scan count 1, logical reads 2
```

Two logical reads per table - the member's claim slice - instead of a scan of
every claim line in the database. This matters because the duplicate check runs
**inside the adjudication loop**, once per leased claim, forever.

## 3. Staff search: prefix predicates stay sargable

`usp_SearchMembers` matches `Mbi LIKE @Query + '%'` and `LastName LIKE @Query + '%'`,
trailing wildcards only, so the optimizer seeks `IX_Members_Mbi` / the
`(LastName, FirstName)` index instead of scanning:

```
Table 'Members'. Scan count 1, logical reads 2
```

Leading wildcards (`'%@Query%'`) would force a scan of the members table on every
keystroke-driven search; the UI sends prefixes and the proc keeps the predicate
sargable. MBI dashes are stripped before the call (`DapperReadStore.NormalizeQuery`)
so the stored (dash-less) form matches.

## 4. Paging: `COUNT(*) OVER()` instead of a second round trip

`usp_SearchMembers` and `usp_ClaimsQueue` return totals as a windowed column
(`COUNT(*) OVER() AS TotalCount`) on every row rather than issuing a separate
count query. One round trip, one plan, and the count is consistent with the page
because it is computed in the same read. At page sizes ≤ 100 the windowed count
adds no measurable cost over the paged read itself.

## 5. What is deliberately *not* optimized

- `usp_GetMember360` runs three result sets in one call; the claims grid is
  capped at 25 rows with YTD totals computed as windowed aggregates over the
  same read - no separate rollup call for the header numbers.
- Rollup procs (`usp_GetDenialRollup`, `usp_GetPlanEnrollmentSummary`) scan
  adjudicated line data by design; they power a dashboard, not a hot loop, and
  the volumes (thousands of rows) don't justify indexed views yet. That is a
  documented next step if line volume grows by orders of magnitude.
- No `NOLOCK` anywhere. The queue tolerates dirty reads in theory, but the
  lease-guarded commit means consistency of the `Adjudicating` → terminal
  transition matters more than a few page reads.
