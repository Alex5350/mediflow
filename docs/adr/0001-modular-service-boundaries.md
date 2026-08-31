# ADR 0001: Modular service boundaries in one repository

- Status: Accepted
- Date: 2026-08-30

## Context

MediFlow has four distinct runtime concerns: an enrollment-side REST API
(members, plans, enrollment applications), a claims-side REST API (intake,
queue, rollups), a background adjudication worker, and a staff-facing web
dashboard. They share the same domain rules, the same database schema, and the
same REST contracts, but they have different consumers, different change
cadences, and very different scaling profiles - the worker drains a queue while
the dashboard serves people.

Two extreme layouts were considered. A single deployable (one process serving
APIs, dashboard, and a hosted worker) is simple but couples interactive request
handling to batch adjudication: a backlog in the worker competes with HTTP
requests, and the worker cannot be scaled or redeployed on its own. Six
repositories (one per service plus shared libraries) is the opposite extreme:
package feeds, versioned library releases, and cross-repo coordinated changes -
real organizational overhead for a codebase of this size and team.

## Decision

One repository and one solution, split into four containerized deployables and
three shared libraries:

- `src/MediFlow.Api` - enrollment-side REST API (members, plans, enrollments).
- `src/MediFlow.Claims.Api` - claims REST API (intake, queue, detail, rollups).
- `src/MediFlow.Worker` - the adjudication worker (outbox leasing, engine, commit).
- `src/MediFlow.Blazor` - the staff dashboard, a pure REST consumer of both APIs.
- `src/MediFlow.Domain` - pure rules and value objects, no infrastructure references.
- `src/MediFlow.Contracts` - REST DTOs shared by the APIs, the dashboard, and tests.
- `src/MediFlow.Infrastructure` - EF Core context, stored procedures, Dapper read
  store, services, and the shared web plumbing.

`src/MediFlow.Mcp` is a fifth executable but not part of the deployment set: it
is a stdio tool host launched on demand by an MCP client, so CI builds images
only for the four deployables above. Boundaries are enforced by project
references (Domain references nothing), not by the network.

## Consequences

- The worker scales independently - `deploy/k8s/worker.yaml` runs two replicas
  against the same leasing procedures, and only the worker's throughput is
  affected by queue depth.
- All services share one SQL Server database. This is a pragmatic middle ground
  rather than a full database-per-service split: services share the DB but own
  disjoint write paths. Enrollment state is written only by the enrollment API
  (`EnrollmentService`), claim intake only by the claims API
  (`ClaimIntakeService`), and adjudication outcomes only by the worker, through
  the leasing procedures. Cross-service reads go through contracts and stored
  procedures, never through another service's DbContext.
- Because the database is shared, exactly one service per environment performs
  boot-time bootstrap (migrations, procedures, optional seed); the others set
  `Database__InitializeOnStartup=false` so concurrent boots do not race.
- A change to a shared library rebuilds every dependent deployable, and the
  boundary is convention plus CI, not a runtime firewall. If a service ever
  needs an independently versioned contract, Contracts is the seam to extract.
