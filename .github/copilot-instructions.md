# Repository guidance for GitHub Copilot — kept short on purpose; everything
# here mirrors docs/onboarding.md.

## Build & test (always run before committing)

```
dotnet build                       # warnings are errors — fix, never suppress
dotnet test tests/MediFlow.Domain.UnitTests
dotnet test tests/MediFlow.IntegrationTests      # needs Docker (Testcontainers)
./scripts/e2e.sh                    # full stack + Playwright; isolated database
```

## Layout

- `src/MediFlow.Domain` — pure rules (no dependencies). Money is US-cents ints.
- `src/MediFlow.Contracts` — REST DTOs shared by APIs, Blazor and the MCP server.
- `src/MediFlow.Infrastructure` — EF Core model, stored procedures (Sql/*.sql are
  embedded resources — rebuild after editing), Dapper read store, seeding.
- `src/MediFlow.Api` — enrollment/members/plans REST API (minimal APIs, TypedResults).
- `src/MediFlow.Claims.Api` — claims + rollups REST API.
- `src/MediFlow.Worker` — adjudication worker (lease → engine → TVP commit).
- `src/MediFlow.Blazor` — staff dashboard (REST consumer of both APIs).
- `src/MediFlow.Mcp` — MCP server exposing read-only ops tools over stdio.

## Conventions that matter

- Rule order in the adjudication engine is semantic (timeliness → coverage →
  duplicates); never reorder casually.
- Only the enrollment API bootstraps the database in multi-service environments.
- Stored procedures use CREATE OR ALTER and are idempotent; apply through
  SqlScriptRunner, never ad hoc.
- Every behavior change ships with its test in the same PR.
- Business keys (claim/application numbers) derive from identity columns —
  insert placeholder, then update in the same transaction.
- Enum values are persisted as ints and mirrored in SQL comments; changing an
  enum requires a migration review.
