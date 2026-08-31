# Design Notes: Hard Problems and How They Were Resolved

A record of the problems that cost real debugging time during the build of
MediFlow, in roughly the order they were hit. Each entry follows the same
shape: symptom, diagnosis, fix, and the general lesson carried forward. The
intent is that a reader evaluating the engineering - or a future maintainer
about to touch the same code - understands not just what the system does but
why it looks the way it does.

## 1. The rules pipeline that silently resolved to empty

**Symptom.** Every claim in an early integration run came back `Paid` -
including a claim for a member with no active enrollment, which should have
been denied CO-27 on sight. Unit tests for every rule passed. The engine
passed. Nothing looked wrong anywhere.

**Diagnosis.** `AdjudicationEngine` takes `IEnumerable<IAdjudicationClaimRule>`
and materializes it into an array. The rules had been registered in the DI
container as their **concrete types** (`AddScoped<FilingTimelinessRule>()`,
etc.), not as the interface. Nothing asked for the concrete types, and nothing
asked for `IEnumerable<IAdjudicationClaimRule>` with anything registered
against it - so the engine's constructor received an empty sequence. An empty
rule chain is a perfectly valid, silently permissive pipeline: no rule denies,
the calculator prices every line, the claim pays. The unit tests never saw it
because they instantiate the engine with an explicit rule array; the bug only
existed at the composition root.

**Fix.** Register the rules against the interface in
`src/MediFlow.Infrastructure/ServiceCollectionExtensions.cs`:

```csharp
services.AddScoped<IAdjudicationClaimRule, FilingTimelinessRule>();
services.AddScoped<IAdjudicationClaimRule, CoverageRule>();
services.AddScoped<IAdjudicationClaimRule, DuplicateClaimRule>();
```

This also made registration order meaningful - DI resolves the `IEnumerable`
in registration order, so the pipeline order (timeliness → coverage →
duplicate) now lives in one visible place. The integration suite grew a test
that submits a real claim through the real API for a member with no
enrollment and asserts the preview comes back `Denied` with a coverage code
(`Valid_submission_is_accepted_and_preview_is_a_dry_run`).

**Lesson.** Unit tests green-lit a composition bug because they never
exercised composition. Any system whose behavior depends on what the
container resolves must have integration tests that assert outcomes **at the
DI boundary** - through the built host, not hand-constructed object graphs.
And a rule chain that fails open deserves extra suspicion: "no rules ran"
should be an impossible state to reach silently.

## 2. T-SQL UPDATE-through-CTE and the OUTPUT clause

**Symptom.** Applying stored procedures at boot failed for
`usp_LeaseNextClaims` with syntax errors, in two separate places, despite the
script being valid to read.

**Diagnosis.** Two T-SQL rules that aren't obvious until violated:

1. An `UPDATE` against a CTE can only `SET` columns that the CTE **projects**.
   The leasing proc's candidate CTE originally selected `Id` and the payload
   columns but not `LeaseToken`/`LeasedUntilUtc`/`Attempts`; updating them
   through the CTE is a compile error. The fix is visible in the shipped
   script - the CTE projects every column it updates (`o.Id, o.LeaseToken,
   o.LeasedUntilUtc, o.Attempts, ...ClaimId`), with a comment marking the
   constraint.
2. In an `UPDATE ... SET ... OUTPUT` statement, the `OUTPUT` clause must come
   **after** `SET`. Ordering it before is a syntax error; the correction is
   mechanical but the error message at boot doesn't point at the ordering.

**Fix.** Corrected both statements in
`src/MediFlow.Infrastructure/Sql/08_usp_LeaseNextClaims.sql`. The surrounding
delivery mechanism was already sound and is what made iterating painless:
`SqlScriptRunner` applies the embedded `.sql` resources in filename order,
splitting on `GO`, and every script is written `CREATE OR ALTER` so the run is
idempotent and re-applied on every boot after EF migrations. The integration
suite asserts at least the eight expected procs exist after bootstrap.

**Lesson.** Database code needs the same fast feedback loop as application
code. Idempotent scripts applied automatically at startup - and verified by a
test - turn "edit, redeploy, watch it fail again" into "edit, run the suite."

## 3. EF Core retry strategy vs. user-initiated transactions

**Symptom.** Claim and enrollment intake threw at first save:

```
The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not
support user-initiated transactions.
```

**Diagnosis.** The DbContext is registered with
`EnableRetryOnFailure` (transient-fault resiliency). EF Core refuses to let a
strategy that may re-execute a unit of work wrap an externally controlled
transaction - a retry that re-ran half of a hand-rolled transaction block
would double-apply writes. The transaction itself was correct (claim + lines +
outbox + audit must commit together or not at all); the problem was who owns
the retry boundary.

**Fix.** Both intake services wrap the entire transactional unit in the
strategy's own retriable scope, in
`src/MediFlow.Infrastructure/Claims/ClaimIntakeService.cs` and
`src/MediFlow.Infrastructure/Enrollment/EnrollmentService.cs`:

```csharp
await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    // ... claim + outbox + audit ...
    await transaction.CommitAsync(ct);
});
```

The strategy and the transaction now nest correctly: a retry repeats the whole
lambda, and the inner transaction guarantees atomicity per attempt.

**Lesson.** Retries and transactions compose in exactly one direction. When a
resiliency framework owns retry, user transactions must run *inside* its
execution strategy - and any multi-write invariant (here: a claim must never
exist without its outbox message) deserves its own test.

## 4. Two APIs racing to migrate a fresh database

**Symptom.** On a clean `docker compose up`, one of the two APIs - whichever
lost the race - crashed in `DatabaseInitializer` with SQL error 1801
("database already exists") or an equivalent boot failure, while the other
booted fine. Which service died varied run to run.

**Diagnosis.** Both APIs ran the EF migration bootstrap concurrently against
the same fresh SQL instance. `CREATE DATABASE` is a race with exactly one
winner; the loser's `MigrateAsync` fails on 1801. The failure was
environmental (first boot only) and timing-dependent, which made it easy to
miss until a cold-start compose run surfaced it.

**Fix.** Two layers, in `src/MediFlow.Infrastructure/Persistence/DatabaseInitializer.cs`
and `docker-compose.yml`:

- **A single bootstrap owner.** In compose, only the enrollment API runs the
  bootstrap; the claims API and worker set `Database__InitializeOnStartup:
  false` and depend on the API having started. The race cannot happen when
  only one contestant exists.
- **Retry for the loser anyway.** `MigrateAsync` failures with SQL 1801 (and
  the other cold-start transients −2, 4060, 233) are retried up to five times
  with a delay, so even a misconfigured deployment converges instead of
  crashing.

**Lesson.** Any "initialize shared state at boot" logic in a multi-service
system needs an explicit ownership decision, plus a tolerance path for when
ownership is violated. Optimistic retry and a single writer are complements,
not alternatives.

## 5. Test-host configuration evaluated too early

**Symptom.** Integration-test factories that overrode
`ConnectionStrings:MediFlowDb` to point at the Testcontainers instance kept
using the development connection string - tests hammered the developer's local
database (or failed to connect) no matter what the factory set.

**Diagnosis.** With minimal hosting, `WebApplicationFactory` applies its
configuration overrides while building the host, but the registration had
captured the connection string **eagerly at registration time**:
`AddDbContext<MediFlowDbContext>(options => options.UseSqlServer(
configuration.GetConnectionString(...)))` closed over the build-time
`IConfiguration`. The override landed after the delegate had already been
evaluated with the old configuration - the read happened once, too early, and
pointed at the wrong database.

**Fix.** Defer the read to resolution time by taking the service provider
inside the registration (visible in
`src/MediFlow.Infrastructure/ServiceCollectionExtensions.cs`):

```csharp
services.AddDbContext<MediFlowDbContext>((sp, options) =>
    options.UseSqlServer(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("MediFlowDb") ?? ...));
```

The test factories (`tests/MediFlow.IntegrationTests/ApiFactories.cs`)
additionally pin the value with `builder.UseSetting(...)` before the app
builds, so both mechanisms agree regardless of ordering.

**Lesson.** Anything read from configuration inside a DI registration should
be resolved lazily, through `IServiceProvider`, unless it genuinely must be
fixed at startup. Eager captures silently defeat every later override source -
test factories, compose environment variables, deploy-time settings.

## 6. Money arithmetic and the unit that fooled its author

**Symptom.** A freshly written benefit test failed against the engine with a
confident-looking assertion error: expected member responsibility of $10.00
where the engine produced $100.00. The engine had no known bugs; the test had
just been written.

**Diagnosis.** All MediFlow money is US-cents integers (`*Cents` fields, exact
arithmetic, decimals only at the edges). Working an OOP-max case by hand -
$1,000 cap, $900 already met - the remaining room was mentally computed as
`100,000 − 90,000 = 1,000` cents, i.e. "$10.00". The subtraction itself was
the slip: the correct difference is **10,000 cents = $100.00**, an order of
magnitude larger. The engine, doing integer cents end to end, was correct.

**Fix.** Recomputed the expectation slowly in cents - remaining room
`100,000 − 90,000 = 10,000` cents = $100.00 - and encoded that in the test
(`Charge_above_fee_schedule_is_capped_at_allowed` and the other accumulator
tests in `tests/MediFlow.Domain.UnitTests/Claims/AdjudicationTests.cs`).
Related hardening from the same episode: `Money.PercentOf` rounds away from
zero at half-cent boundaries, pinned by its own tests, so coinsurance splits
are deterministic in engine, SQL commit and assertions alike.

**Lesson.** The tests were right and the author's arithmetic wasn't - which is
the point of encoding expectations as executable assertions instead of
trusting a mental pass. In a cents-based system, hand computations should be
written in raw cents and only converted to dollars at the final step, and any
rounding convention (away-from-zero vs. banker's) must be chosen once and
unit-tested, or three components will disagree by a cent somewhere.

## 7. Testcontainers against a colima Docker runtime

**Symptom.** The integration suite (one SQL Server container per assembly via
Testcontainers) hung on container start on the development machine - the
container was demonstrably up and accepting connections, but the fixture never
proceeded, until the global timeout killed the run.

**Diagnosis.** Two environmental facts collided. The machine's Docker runtime
is colima, not Docker Desktop: Testcontainers only finds the colima socket if
`docker.host` points at it (`unix:///Users/alex/.colima/default/docker.sock` in
`~/.testcontainers.properties`). And the `Testcontainers.MsSql` module's
default wait strategy shells out to a **host-installed** `sqlcmd` binary to
probe readiness - a binary that doesn't exist on the host, and an approach
that also misbehaves when the container's port mapping lands on a
non-default interface under colima. Separately, the ryuk resource-reaper
session was disabled (`ryuk.disabled=true`) for the same runtime-socket
reason.

**Fix.** In `tests/MediFlow.IntegrationTests/TestDatabaseFixture.cs`:

- replace the module's wait strategy with `Wait.ForUnixContainer()`, which
  waits on the container itself rather than a host tool;
- poll readiness with plain connection attempts - up to 60 tries, two seconds
  apart, opening a `SqlConnection` and returning on the first success -
  instead of trusting the module's readiness signal;
- keep the image configurable via `MEDIFLOW_TEST_SQL_IMAGE`: the default is
  `azure-sql-edge` (arm64-native for local runs) while CI overrides it to
  `mssql/server:2022` on x64 runners, so the same fixture works in both
  places.

**Lesson.** Container-based test infrastructure is only as portable as its
weakest assumption about the host. Prefer readiness probes that go through
the real client (`SqlConnection.Open`) over host-shell heuristics, and treat
the container runtime itself - Docker Desktop vs. colima - as a supported
platform with its own documented configuration, not an edge case.

---

### Themes worth keeping

Three threads run through all seven: **composition must be tested, not just
units** (problems 1, 3, 5 - all invisible to code that constructed its own
dependencies); **cold starts and shared resources are a distinct failure
domain** (2, 4, 7 - first boot, first container, first concurrent writer);
and **determinism is a feature you build** (6 - integer cents, one rounding
rule, hand-computed expectations, byte-identical seeds). The seed data's
guarantees - real check digits, real engine pricing - exist because of these
threads: demo data that doesn't obey the domain's own validation is a lie
that costs the next maintainer an afternoon.
