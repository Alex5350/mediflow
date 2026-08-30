# The MediFlow MCP Server

`src/MediFlow.Mcp` packages MediFlow's operational surfaces as a
[Model Context Protocol](https://modelcontextprotocol.io) server over stdio,
built on the ModelContextProtocol C# SDK (2.2.0). It exposes the same
read-only views the Blazor dashboard uses — member search, member 360, the
claims work queue, claim detail with line-level remittance, denial rollups —
plus two dry-run capabilities that run real domain logic **without writing**:
enrollment eligibility checks and adjudication previews.

## Why it exists

Operations staff increasingly answer questions from inside a chat surface or
IDE rather than a browser. Pointing an MCP client at MediFlow gives that
surface:

- the same stored-procedure-backed reads the dashboard renders (Dapper
  `IReadStore` and `IClaimDetailsService` resolve identically in both hosts);
- enrollment eligibility checks that run `EnrollmentService.CheckEligibilityAsync`
  — the exact code path behind `POST /api/v1/enrollments/eligibility` — without
  creating an application;
- adjudication previews that run the real `AdjudicationEngine` end to end and
  report the decision it *would* make, without touching the queue, the
  accumulators or the audit trail.

Everything a staff member can do from chat, they could already do from the
dashboard; nothing a staff member can do from chat changes the database.

## Tools

Eight tools are advertised, all defined as static methods on `MediFlowTools`
(`src/MediFlow.Mcp/MediFlowTools.cs`). All results are plain text, formatted
for reading in a chat window.

| Tool | Parameters | Returns |
|------|------------|---------|
| `search_members` | `query` (MBI or last/first name prefix, e.g. `1EG4` or `Abernathy`), `page` (default 1) | Paged member matches with id, name, MBI, DOB, Part B entitlement date, state. |
| `get_member_360` | `memberId` (from `search_members`) | Active coverage, application count, recent claim statuses, YTD plan-paid and member-share totals. |
| `check_enrollment_eligibility` | `memberId`, `planId`, `effectiveDate` (`yyyy-MM-dd`), `sepReason` (0 none, 1 moved, 2 lost coverage, 3 dual eligible, 4 LIS, 5 five-star; default 0) | `ELIGIBLE` or `NOT ELIGIBLE` with every rule violation (`PlanNotOfferedForYear`, `PartBNotEffective`, `AlreadyEnrolledSameType`, `OutsideEnrollmentWindow`, `EffectiveDateNotFirstOfMonth`). Nothing is saved. |
| `claims_queue` | `statuses` (comma-separated filter: `Received,Adjudicating,Paid,Denied,Pended,DeadLettered`; optional), `page` | Paged work queue with claim number, status, member, service date, charge, denial code. |
| `get_claim` | `claimNumber` (e.g. `CLM-2026-000511`) | Full claim detail: header totals, line-level charge/allowed/plan-paid/member-owes with adjustment codes, and the audit trail. |
| `preview_adjudication` | `claimId` (numeric id) | The decision the engine *would* make: status, claim-level denial if any, per-line amounts, and post-decision accumulator values. Explicitly tagged "Dry run — nothing was committed." |
| `denial_rollup` | `year` (four-digit service year; defaults to current) | Denial counts and charged/unpaid dollars grouped by adjustment code. |
| `explain_denial_code` | `code` (`CO-18`, `PR-1`, enum name, or fragment) | Plain-language description from `DenialCodeDescriptions`, or the list of known codes. |

## Safety model

The server is **read-only by design**:

- Six of the eight tools (`search_members`, `get_member_360`, `claims_queue`,
  `get_claim`, `denial_rollup`, `explain_denial_code`) query through
  `IReadStore`/`IClaimDetailsService` or the static code-description table —
  no write paths are reachable.
- The two engine invocations are both dry runs.
  `check_enrollment_eligibility` calls `CheckEligibilityAsync`, which validates
  and returns; it never constructs an application.
  `preview_adjudication` calls `IClaimAdjudicationRunner.PreviewAsync`, which
  assembles the same `AdjudicationRequest` the worker assembles and runs the
  same engine — then returns a DTO instead of calling
  `usp_RecordAdjudicationResult`. An integration test pins this contract:
  `Valid_submission_is_accepted_and_preview_is_a_dry_run`
  (`tests/MediFlow.IntegrationTests/ApiTests.cs`) asserts the preview returns
  a decision while the claim itself is **still** `Received` afterwards.
- All writes — claim submission, enrollment submission and decisions,
  adjudication commits — remain behind the APIs, which enforce API keys
  (`ApiKeyMiddleware`) and the audit trail.

## Running it

The server talks to the same SQL Server database as the rest of the stack, so
bring the demo environment up first:

```bash
./scripts/start.sh          # SQL container + both APIs + worker + dashboard
```

or `docker compose up --build`. The server reads
`ConnectionStrings:MediFlowDb` from its `appsettings.json`
(`Server=localhost,1433;Database=MediFlow;...`) and, unlike the APIs, sets
`Database:AutoMigrate/InitializeOnStartup: false` — it assumes an
already-bootstrapped database and never modifies schema.

Smoke test end to end without any MCP client:

```bash
./scripts/mcp-smoke.sh
```

The script builds the server, then drives the raw stdio JSON-RPC protocol —
`initialize`, `notifications/initialized`, `tools/list`, and two `tools/call`
invocations (`explain_denial_code CO-18`, `claims_queue`) — against the built
binary, asserting each response. It is the quickest way to prove the server,
database and tool surface are all healthy.

## Wiring it into clients

### VS Code / GitHub Copilot

Add to `.vscode/mcp.json` (workspace) or the user MCP config:

```json
{
  "servers": {
    "mediflow": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/MediFlow.Mcp", "--no-build"],
      "cwd": "/Users/alex/portfolio/mediflow",
      "env": {
        "ConnectionStrings__MediFlowDb": "Server=localhost,1433;Database=MediFlow;User ID=sa;Password=MediFlow!Dev1;TrustServerCertificate=True;Encrypt=True"
      }
    }
  }
}
```

Build once (`dotnet build`) so `--no-build` resolves; or drop `--no-build` and
let the first launch compile. Adjust `cwd` to wherever the repository lives —
`--project` is resolved against it.

### Generic stdio config (any MCP-capable host)

Equivalent stdio server entry for clients that take a command/args/env shape:

```json
{
  "mcpServers": {
    "mediflow": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/Users/alex/portfolio/mediflow/src/MediFlow.Mcp",
        "--no-build"
      ],
      "env": {
        "ConnectionStrings__MediFlowDb": "Server=localhost,1433;Database=MediFlow;User ID=sa;Password=MediFlow!Dev1;TrustServerCertificate=True;Encrypt=True"
      }
    }
  }
}
```

With an absolute `--project` path no working directory is required, which
matters because MCP hosts launch servers from arbitrary locations (see below).

## Example session

Question: *"who owes what on claim CLM-2026-000481?"*

The client calls `get_claim`:

```text
tools/call get_claim {"claimNumber": "CLM-2026-000481"}
```

```
CLM-2026-000481 — Paid
Member: Whitfield, Margaret (MBI 1EG4TE5MK73) · Plan MFP-2601 · NPI 1234567893
Service 2026-03-12 · received 2026-03-15 09:40 UTC
Totals: charged $812.00, plan paid $144.00, member owes $30.00
  1. 99214: charged $200.00, allowed $174.00, plan pays $144.00, member owes $30.00 [PR-2]
  2. S9994: charged $612.00, allowed $0.00, plan pays $0.00, member owes $0.00 [CO-96]
Audit: Submitted (03-15 09:41, provider-portal) → Adjudicated (03-15 12:10, worker)
```

The member asks why the second line paid nothing, so the client follows up:

```text
tools/call explain_denial_code {"code": "CO-96"}
```

```
CO-96: CO-96 — Non-covered charge (not on the plan fee schedule).
```

and can close the loop with `preview_adjudication` on the claim id to show
what would happen if it were re-run today — a dry run that commits nothing.

## Engineering notes

Two details in `Program.cs` are load-bearing:

- **stdout is the protocol.** With stdio transport, anything the process
  writes to stdout corrupts the JSON-RPC stream. Logging providers are
  cleared at startup (`builder.Logging.ClearProviders()`) and Microsoft-level
  logging is filtered to warnings so nothing chatters into the channel.
- **content root is pinned.** MCP hosts launch servers from arbitrary working
  directories; the host builder pins `ContentRootPath` to
  `AppContext.BaseDirectory` so `appsettings.json` always loads from the
  build output next to the binary, regardless of the caller's cwd.
