# ADR 0008: Four-tier testing strategy

- Status: Accepted
- Date: 2026-08-30

## Context

MediFlow's risk concentrates in specific places: pure eligibility and
adjudication rules that must be deterministic; SQL objects (leasing, the TVP
commit, the read procedures) whose correctness C# tests cannot observe; the DI
composition wiring the rule chain, both APIs, and the middleware pipeline; and
the end-to-end flow from a browser click to a paid claim. One style of test
cannot cover all four, and the cheapest tier should absorb the most cases.

## Decision

Four tiers, each owning one kind of risk.

**1. Domain unit tests** (`tests/MediFlow.Domain.UnitTests`, 65 cases across
enrollment rules, adjudication rules and benefit math, and the MBI/NPI/Money
value objects). No I/O, no clock — `EnrollmentRules` takes `asOfUtc` and
`submittedAtUtc` as arguments, so window logic is tested against fixed dates.
CI enforces an 80% line-coverage threshold on this project
(`-p:Threshold=80 -p:ThresholdType=lines` with XPlat Code Coverage).

**2. Integration tests** (`tests/MediFlow.IntegrationTests`, 15 tests). One
Testcontainers SQL Server container per assembly run (azure-sql-edge on arm64
laptops, `mssql/server:2022-latest` via `MEDIFLOW_TEST_SQL_IMAGE` on x64 CI
runners); the fixture migrates, applies the stored procedures, and runs the
deterministic seeder. Two `WebApplicationFactory` hosts boot the real APIs
against that container with API-key enforcement left on. This tier owns
everything SQL- or DI-shaped: procedure behavior, the lease/commit/fail-lease
lifecycle (including the wrong-token `THROW 51002` rejection), intake
validation through the real HTTP surface, and the dry-run preview.

**3. bUnit component tests** (`tests/MediFlow.Blazor.UnitTests`, 10 cases) for
the presentational shell — status badges across all six claim states plus the
enrollment variant, and pager behavior — without a browser.

**4. Playwright E2E** (`e2e/`, 8 specs). `scripts/e2e.sh` is self-bootstrapping:
it starts SQL Server if needed, creates an isolated `MediFlow_E2E` database,
builds, launches all four services (APIs on 8080/8081, dashboard on 8090, plus
the worker), waits on health endpoints, runs the suite, and tears everything
down. Dev and demo data are never touched. CI runs the same script and uploads
traces on failure.

## Why the DI boundary needs tier 2

The rules pipeline (ADR 0002) once failed silently: rules registered as
concrete types produced an empty `IEnumerable<IAdjudicationClaimRule>`, so the
engine denied nothing. Every domain unit test stayed green — they construct the
engine with explicit rule instances. What failed was an integration test
asserting that a claim for a member with no enrollment previews as a coverage
denial. Wiring is behavior; only tests that resolve from the real container
catch composition regressions, so integration tests assert outcomes through
the hosted APIs rather than reconstructed object graphs.

## Consequences

- Most new tests land in tier 1 (fast, deterministic); tiers 2-4 grow only
  when SQL, wiring, or the user journey changes.
- House rule, enforced by the PR template checkbox: a behavior change ships
  with a test in the same PR, `dotnet build` is clean (warnings are errors),
  and `./scripts/e2e.sh` is green or explicitly marked N/A with a reason.
- Integration and E2E tiers need Docker — the accepted cost of testing real
  SQL Server semantics instead of an in-memory substitute.
