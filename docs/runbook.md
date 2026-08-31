# Runbook - operating MediFlow

Operational reference for the four services, what to watch, and what to do when
something goes wrong. The Ops page in the dashboard (`/ops`) mirrors most of
these signals; this document explains the mechanics behind them.

## Service inventory

| Service | Project | Port | Health | Notes |
|---|---|---|---|---|
| Enrollment API | `src/MediFlow.Api` | 8080 | `/health`, `/health/live`, `/health/ready` | Members, plans, enrollment. Owns database bootstrap (`Database__InitializeOnStartup=true`). |
| Claims API | `src/MediFlow.Claims.Api` | 8081 | `/health`, `/health/live`, `/health/ready` | Claims queue, detail, preview, re-queue, rollups. |
| Adjudication worker | `src/MediFlow.Worker` | - | none (no HTTP host) | Drains the outbox; console/OTLP metrics only. |
| Blazor dashboard | `src/MediFlow.Blazor` | 8090 | `/health/live` | REST consumer of both APIs. |

Health semantics (both APIs): `/health/live` has no checks and answers as long
as the process is up; `/health/ready` runs the EF Core `DbContext` check
(`AddDbContextCheck<MediFlowDbContext>` tagged `ready`) and fails when the
database is unreachable; `/health` returns the ready checks as a JSON body.
Kubernetes manifests wire these directly into liveness/readiness probes.

## Observability

- **Logs.** Serilog console on every service, enriched with a `Service` property
  (`mediflow-api`, `mediflow-claims-api`, `mediflow-worker`, `mediflow-blazor`),
  format `[HH:mm:ss LVL] service message`. Local script runs write to
  `/tmp/mediflow-<service>.log`; containers emit to stdout (compose:
  `docker compose logs -f worker`; k8s: `kubectl logs -n mediflow deploy/mediflow-worker`).
- **Traces and metrics.** The APIs export ASP.NET Core, HttpClient, and SqlClient
  telemetry; the worker exports runtime instrumentation - all through
  OpenTelemetry. Export happens **only when `OTEL_EXPORTER_OTLP_ENDPOINT` is
  set** (compose/Azure wire a collector; without it, telemetry stays local and
  Serilog owns the console). The Azure deployment (`infra/main.bicep`)
  destinations are Log Analytics + Application Insights.
- **Custom worker metrics** (meter `MediFlow.Worker`, defined in
  `AdjudicationMetrics`):

| Instrument | Type | Meaning |
|---|---|---|
| `mediflow.claims.adjudicated` | counter (`{claim}`, tagged `status`) | Claims completed, split Paid/Denied |
| `mediflow.adjudication.duration` | histogram (`ms`) | Engine wall time per claim |
| `mediflow.adjudication.failures` | counter (`{claim}`) | Attempts that failed and were retried (includes lease-rejected commits) |

- **Queue signals without a collector.** `GET /api/v1/rollups/dashboard` (the
  Claims API; needs `X-Api-Key` outside Development) returns `usp_GetDashboardStats`:
  `outboxDepth`, `claimsAdjudicating` (in flight), `claimsOpen`,
  `claimsDeadLettered`, `claimsPaid30d`, `claimsDenied30d`, `enrollmentsPending`,
  `enrollmentsActive`, YTD dollars. The dashboard Ops page tiles the same data.

Worker mechanics worth memorizing: poll every 5 s (15 s when idle), lease
batches of up to 10 claims, **lease duration 2 minutes**, failure path releases
the lease and backs off 30 s, **5 failed attempts dead-letter a claim**.

## Incident playbooks

### 1. Queue not draining

Symptoms: `outboxDepth` flat or climbing, `claimsOpen` not falling,
`claimsAdjudicating` at 0, dashboard tiles stale.

1. Check the worker is alive and reading its logs - you want `Adjudicated
   CLM-… → Paid/Denied` lines. `Adjudication batch failed - retrying next poll`
   points at database connectivity or a systemic engine error; the exception
   follows on the next line.
2. Confirm the database is reachable from other services (`/health/ready` on
   either API). If the DB is down, everything waits; `DatabaseInitializer`
   probes for up to 60 × 2 s at boot.
3. Restart the worker. This is always safe: leasing is atomic in SQL
   (`usp_LeaseNextClaims` uses `READPAST`/`UPDLOCK`), and an abandoned lease
   simply lapses after 2 minutes and becomes leasable again. Multiple replicas
   scale out safely for the same reason.
4. If depth still grows with a healthy worker, look for outbox rows stuck with
   `LastError` set and `AvailableAtUtc` in the future (backoff), or claims
   oscillating Received → Adjudicating (see playbook 6).

### 2. Dead-lettered claims need review

Symptoms: Dead letters tile > 0, or the Ops page lists claims with 5 attempts.

1. Open **Ops** (`/ops`) or the claims queue filtered to `DeadLettered`
   (`/claims`, Dead-lettered filter).
2. Open the claim detail. The audit trail's `DeadLettered` entry plus the
   outbox `LastError` (seeded examples read "fee schedule lookup timeout") give
   the root cause; fix that first - a re-queue without a fix just burns five
   more attempts.
3. Click **Re-queue for adjudication** on the detail page. That calls
   `POST /api/v1/claims/{id}/adjudicate`, which inserts a fresh, immediately
   available outbox message (attempts reset). The page polls every 3 s while
   the claim is open, so you watch the decision land.
   Only `Received` and `DeadLettered` claims can be re-queued; anything else
   returns `409`.
4. Equivalent CLI (Production, where the key is enforced):

   ```bash
   curl -X POST -H "X-Api-Key: $KEY" http://localhost:8081/api/v1/claims/123/adjudicate -i
   ```

### 3. API returns 401 everywhere

The APIs reject requests whose `X-Api-Key` does not match `Api__Keys`. Causes,
in order of likelihood:

- The caller's key and `Api__Keys` disagree - keys are a comma-separated list,
  and each service reads its own environment. Verify what the container
  actually has (`docker compose exec api printenv Api__Keys`).
- The Blazor dashboard's `Api__Key` (singular - the dashboard is a client)
  mismatches the APIs' `Api__Keys`. The dashboard's API calls then 401 while
  direct API calls succeed.
- Running in Development where `Api:Required=false` and expecting enforcement -
  the check is skipped there by design; test auth against a Production-mode
  deployment (compose) or the integration suite, which forces the key on.

Missing keys return a `401` ProblemDetails body ("A valid X-Api-Key header is
required."); note the anonymous prefixes `/health`, `/alive`, `/openapi`,
`/scalar` always pass without a key - that is intentional, not a bypass.

### 4. API returns 429

The shared host configures a fixed-window limiter: 100 permits per 1-minute
window, `QueueLimit = 0` - excess requests are rejected immediately with `429`,
not queued. A burst from a script or a poll loop will trip it. Remediate the
caller (back off, page through results instead of re-scanning) rather than the
service; production deployments tune per-consumer limits.

### 5. Database bootstrap race at startup

Only the enrollment API initializes the database. The claims API and worker
run with `Database__InitializeOnStartup=false` in every environment
(`scripts/start.sh`, `docker-compose.yml`, `deploy/k8s/config.yaml`,
`infra/main.bicep`) precisely so they cannot race migrations or seeding on a
shared database.

If you see migration failures during boot:

- `DatabaseInitializer` retries `MigrateAsync` on SQL errors `1801` (database
  already exists), `-2` (timeout), `4060`, and `233` - up to 5 attempts, 5 s
  apart. Transient races resolve themselves; you will see "Migration race with
  a sibling service" warnings and then success.
- A new service or manual `dotnet ef database update` racing the enrollment API
  is the usual cause of persistent `1801` noise. Do not run migrations by hand
  while the stack boots.
- If the enrollment API itself cannot connect, it probes for 60 × 2 s against
  `master` before failing - in compose, check that the `sql` container passed
  its healthcheck (`docker compose ps`).

### 6. Claim stuck in Adjudicating

A claim shows `Adjudicating` only while a worker holds its lease.

- **Worker died mid-lease** - nothing to do. The lease lapses after 2 minutes
  and the next batch re-leases the claim (the outbox row stays pending until
  completed).
- **Worker alive but the claim keeps failing** - each failed attempt releases
  the lease with a 30 s backoff and increments the attempt counter; after 5
  attempts the claim is dead-lettered (via the failure path, or swept by
  `usp_LeaseNextClaims` if the worker crashed without releasing). Move to
  playbook 2.
- **Commit rejected with SQL error 51002** means the lease expired mid-commit;
  the worker logs "Lease rejected" and leaves the claim queued. Self-healing.

## Escalation thresholds

Starting points for demo-scale operations - tune to the environment:

| Signal | Threshold | Action |
|---|---|---|
| Outbox depth (`/api/v1/rollups/dashboard`) | > 50 sustained 10 min | Playbook 1; check worker logs and DB health |
| Claim in `Adjudicating` | > 5 min (lease is 2 min) | Playbook 6; confirm a worker is running |
| New dead letters | any | Playbook 2 same day - root cause before re-queue |
| `mediflow.adjudication.failures` rate | climbing across polls | Pull worker logs; the exception names the rule or query failing |
| `/health/ready` | failing on either API | Database availability; page if not self-healing in 2 min |

## Verifying a release

CI (`.github/workflows/ci.yml`) is the release gate; a green run means:

1. **build + format** - compiles with warnings-as-errors and passes
   `dotnet format --verify-no-changes`.
2. **unit tests** - domain suite green with line coverage ≥ 80 % enforced; bUnit
   component suite green.
3. **integration tests** - full suite against a Testcontainers SQL Server.
4. **e2e** - Playwright against the self-bootstrapped stack.
5. **docker build** - all four images build.

`publish.yml` then pushes the four images to GHCR (tagged by commit SHA and
branch). Post-deploy smoke, in order:

```bash
curl -s http://localhost:8080/health | jq .          # ready + db check
curl -s http://localhost:8081/health | jq .
curl -s -H "X-Api-Key: $KEY" http://localhost:8081/api/v1/rollups/dashboard | jq .
```

Then one human pass: dashboard loads, submit a claim at `/claims/submit`, and
watch its detail page flip from Received to Paid/Denied within seconds - that
exercises APIs, database, worker, and dashboard in one motion.
