# Onboarding - MediFlow day one

MediFlow is a Medicare enrollment and claims adjudication reference platform:
two REST APIs, a background adjudication worker, a Blazor dashboard, and a SQL
Server database, all wired together with a deterministic seed so the demo data
is the same on every machine. This guide takes you from a clean clone to a
running stack, shows you where everything lives, and walks one feature
end-to-end.

## Prerequisites

| Tool | Version | Why |
|---|---|---|
| .NET SDK | 10 (`net10.0`) | All projects target `net10.0` (`Directory.Build.props`) |
| Docker | any recent | SQL Server container, Testcontainers integration tests, E2E stack |
| Node.js | 22 | Playwright E2E suite in `e2e/` |
| `sqlcmd` | any | `scripts/e2e.sh` uses it to recreate the isolated E2E database (`brew install sqlcmd` on macOS) |

`dotnet tool restore` is optional - the only tool pinned in `dotnet-tools.json`
is `dotnet-ef` 10.0.11, needed only when you change EF migrations.

## First run

```bash
git clone <repo-url> mediflow && cd mediflow
./scripts/start.sh
```

`scripts/start.sh` does everything:

1. Starts an `azure-sql-edge` container named `mediflow-sql` on port 1433
   (`sa` password `MediFlow!Dev1`), unless one is already running.
2. Builds the solution.
3. Starts all four services in the background. Only the enrollment API
   bootstraps the database (EF migrations + stored procedures + seed); the
   claims API and worker are launched with `Database__InitializeOnStartup=false`
   so they never race it.
4. Waits until `/health/live` answers on all three HTTP services.

When it finishes you should see:

```
MediFlow is up:
  Dashboard    http://localhost:8090
  Enrollment API  http://localhost:8080/scalar/v1  (X-Api-Key not required in Development)
  Claims API      http://localhost:8081/scalar/v1
```

Open the dashboard first. Both APIs expose interactive Scalar docs at
`/scalar/v1` and raw OpenAPI at `/openapi/v1.json`. The services run in
`Development`, where `Api:Required=false` (from each API's
`appsettings.Development.json`) - the `X-Api-Key` check is skipped locally.
Outside Development the key is enforced; see `docs/security.md`.

## What the seed gives you

Seeding is deterministic (`MediFlowDataSeeder`, fixed LCG seed), and paid claims
are priced by the real `AdjudicationEngine`, so the data is internally
consistent. On a fresh database you get:

- **160 members** with synthetic MBIs (CMS-safe character set) and real Luhn
  check-digit NPIs for providers.
- **12 plans for contract year 2026** (8 Medicare Advantage including a
  five-star plan, 4 standalone PDP), plus 4 legacy 2025 plans and 2025/2026 fee
  schedules.
- **Roughly 500 claims** (~510 measured in `docs/query-tuning.md`) spanning the
  lifecycle: Paid (~80%), Denied (timely filing, coverage terminated, duplicate),
  open Received claims sitting in the outbox queue, a few Pended, and a
  dead-lettered set with five failed attempts for the operations page.
- Enrollment applications in every state: Active, PendingVerification, Denied.

The seeded Received claims are immediately available to the worker, so within
seconds of boot the "Open claims" tile on the dashboard drops as the worker
drains the queue - a good first demo of the live pipeline.

## Repository layout

| Path | Contents |
|---|---|
| `src/MediFlow.Domain` | Rules engine: enrollment eligibility, adjudication rules, benefit math, MBI/NPI/Money value objects. No infrastructure dependencies. |
| `src/MediFlow.Contracts` | Request/response DTOs shared by APIs, dashboard, and tests. |
| `src/MediFlow.Infrastructure` | EF Core context + migrations, Dapper read store, stored procedures (`Sql/*.sql`, embedded resources), claim intake, adjudication gateway, API-key middleware, database initializer/seeder. |
| `src/MediFlow.Api` | Enrollment/members/plans REST API (port 8080). |
| `src/MediFlow.Claims.Api` | Claims + rollups REST API (port 8081). |
| `src/MediFlow.Worker` | Adjudication worker: leases claims in batches, runs the engine, commits results transactionally. |
| `src/MediFlow.Blazor` | Server-side Blazor dashboard (port 8090), a REST consumer of both APIs. |
| `src/MediFlow.Mcp` | MCP server exposing read-only operations tools over stdio. |
| `tests/MediFlow.Domain.UnitTests` | xUnit, no I/O. |
| `tests/MediFlow.IntegrationTests` | xUnit + Testcontainers SQL Server + in-memory API hosts. |
| `tests/MediFlow.Blazor.UnitTests` | bUnit component tests. |
| `e2e/` | Playwright specs + helpers. |
| `scripts/` | `start.sh`, `stop.sh`, `e2e.sh`, `mcp-smoke.sh`. |
| `deploy/k8s` | Namespace, config, and deployment manifests for the four services. |
| `infra/` | `main.bicep` Azure Container Apps deployment. |
| `docs/` | This guide, `security.md`, `runbook.md`, `testing.md`, `query-tuning.md`. |

## The four test tiers

| Tier | Project | What it covers | Command |
|---|---|---|---|
| 1. Domain unit | `tests/MediFlow.Domain.UnitTests` | Rules, state machines, value objects - pure, fast, no I/O | `dotnet test tests/MediFlow.Domain.UnitTests` |
| 2. UI components | `tests/MediFlow.Blazor.UnitTests` | Presentational Blazor components via bUnit | `dotnet test tests/MediFlow.Blazor.UnitTests` |
| 3. Integration | `tests/MediFlow.IntegrationTests` | Stored procedures, gateway leasing, and both API hosts against a real SQL Server container | `dotnet test tests/MediFlow.IntegrationTests` |
| 4. End-to-end | `e2e/` | Playwright driving the real dashboard, APIs, and worker | `./scripts/e2e.sh` |

Notes:

- The integration suite starts its own SQL container (Docker required). It
  defaults to `azure-sql-edge`; set `MEDIFLOW_TEST_SQL_IMAGE` to override (CI
  uses `mcr.microsoft.com/mssql/server:2022-latest` on x64 runners).
- `./scripts/e2e.sh` boots the whole stack against an isolated
  `MediFlow_E2E` database - your dev/demo data is never touched. Add `--headed`
  to watch the browser or `--ui` for the interactive Playwright UI. First local
  run needs a one-time `cd e2e && npm install && npx playwright install`.
- Plain `dotnet test` at the repo root runs tiers 1-3 together.

Details and recipes for writing new tests at each tier live in
`docs/testing.md`. The PR rule is simple: a behavior change ships with the test
that validates it.

## MCP smoke test

With the stack running (the MCP server reads the demo database), verify the
stdio JSON-RPC surface:

```bash
./scripts/mcp-smoke.sh
```

It drives `initialize`, `tools/list` (8 tools advertised), and two `tools/call`
invocations (`explain_denial_code`, `claims_queue`), failing loudly on any
missing response. The tools themselves are read-only by design; adjudication is
exposed only as a dry-run preview.

## Adding a feature end-to-end

The most common change here is a new read endpoint backed by a stored procedure.
Concretely: an aging report of open claims, `GET /api/v1/claims/aging`. The path
touches every layer, in this order.

**1. The stored procedure** - add `src/MediFlow.Infrastructure/Sql/10_usp_ClaimAging.sql`.
The `Sql` folder is included as embedded resources
(`Sql/**/*.sql` in `MediFlow.Infrastructure.csproj`) and applied in filename
order by `SqlScriptRunner` on every boot, so the numeric prefix matters - `10_`
lands after the types and other procs. Scripts must be idempotent
(`CREATE OR ALTER`), because the runner executes on every service boot and in
the integration fixture. Because the file is embedded at build time, you must
rebuild for a new or changed script to be picked up.

**2. The contract** - add a DTO to `src/MediFlow.Contracts`
(e.g. `Claims/ClaimDtos.cs`). Follow the existing paging pattern: the proc
returns `COUNT(*) OVER() AS TotalCount` on each row and the store wraps it in
`PagedResult<T>`.

**3. The read store** - add the method to `IReadStore` and `DapperReadStore`
(`src/MediFlow.Infrastructure/Data/DapperReadStore.cs`). One method, one proc,
parameters as an anonymous object - Dapper parameterizes everything:

```csharp
public async Task<PagedResult<ClaimAgingRowDto>> ClaimAgingAsync(int olderThanDays, int pageIndex, int pageSize, CancellationToken ct = default)
{
    await using DbConnection connection = await connectionFactory.CreateOpenConnectionAsync(ct);
    var command = new CommandDefinition("dbo.usp_ClaimAging",
        new { OlderThanDays = olderThanDays, PageIndex = Math.Max(1, pageIndex), PageSize = Math.Clamp(pageSize, 1, 100) },
        cancellationToken: ct);
    var rows = (await connection.QueryAsync<ClaimAgingRowDto>(command)).AsList();
    var total = rows.Count > 0 ? rows[0].TotalCount : 0;
    return new PagedResult<ClaimAgingRowDto>(rows, total, pageIndex, pageSize);
}
```

**4. The endpoint** - extend the minimal-API module (`ClaimsModule.cs` for
claims, `MembersModule`/`PlansModule`/`EnrollmentModule` for the enrollment
API). Copy the shape: route group, typed lambda, `TypedResults`:

```csharp
group.MapGet("/aging", async Task<Ok<PagedResult<ClaimAgingRowDto>>> (
    IReadStore readStore, int olderThanDays = 7, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
{
    var rows = await readStore.ClaimAgingAsync(olderThanDays, page, pageSize, ct);
    return TypedResults.Ok(rows);
});
```

**5. Tests at each tier** - an integration test that the proc returns seeded
rows and filters correctly (pattern: `StoredProcTests` in
`tests/MediFlow.IntegrationTests`), an API test through the real host with the
`X-Api-Key` header (pattern: `ApiTests.cs` + the factories in
`ApiFactories.cs`), a unit test if you added any domain logic, and an E2E spec
if the dashboard surfaces it. Nothing else is needed to wire the endpoint -
DI, auth, rate limiting, health checks, and OpenAPI come from the shared
`AddMediFlowWebService`/`UseMediFlowWebService` host plumbing.

## Day-to-day commands

| Task | Command |
|---|---|
| Tail service logs | `tail -f /tmp/mediflow-api.log` (also `mediflow-claims`, `mediflow-worker`, `mediflow-blazor`) |
| Stop the four services | `./scripts/stop.sh` (leaves the SQL container running) |
| Remove the SQL container too | `docker rm -f mediflow-sql` |
| Reset the demo database | `SEED_RESET=true ./scripts/start.sh` - drops, re-migrates, and re-seeds `MediFlow`. Without the flag, seeding is skipped whenever members already exist. |
| Query the database directly | `sqlcmd -S localhost,1433 -U sa -P 'MediFlow!Dev1' -d MediFlow -Q "EXEC dbo.usp_GetDashboardStats"` |

Process PIDs are kept in `/tmp/mediflow-<service>.pid`; E2E-run logs are
written to `/tmp/mediflow-e2e-*.log`.

## Alternative: docker compose

`docker compose up --build` brings up the same five containers (SQL, both APIs,
worker, dashboard) with the same deterministic seed. It runs the services in
`Production`, so the API key is enforced: copy `.env.example` to `.env` and set
`MEDIFLOW_API_KEYS` (the committed `mediflow-dev-key` default is documented as
local-demo-only). Ports match the script-based stack: 8080, 8081, 8090, 1433.
On x64 hosts set `SQL_IMAGE=mcr.microsoft.com/mssql/server:2022-latest`.

## Beyond local: Kubernetes and Azure

- `deploy/k8s` contains namespace/config/deployment manifests for the four
  services against images from GHCR, with health probes, resource limits, and
  non-root `securityContext` entries. The `api` deployment owns database
  bootstrap; everything else ships `Database__InitializeOnStartup=false`.
- `infra/main.bicep` deploys the full Azure reference architecture: a Container
  Apps environment with the four apps, Azure SQL, Log Analytics + Application
  Insights, and a Key Vault holding the API keys:

  ```bash
  az deployment group create -g <rg> -f infra/main.bicep -p @infra/main.parameters.json
  ```

Images are published to GHCR by `.github/workflows/publish.yml` on every push
to `main`.

## Where to go next

- `docs/security.md` - the auth model and the DevSecOps pipeline.
- `docs/runbook.md` - service inventory, metrics, and incident playbooks.
- `docs/testing.md` - how to run and write each tier of test.
- `docs/query-tuning.md` - the index reasoning behind the hot stored procedures.
