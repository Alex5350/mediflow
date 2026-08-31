# Testing

Four tiers, each owning a distinct failure class. The rule that governs all of
them: **a behavior change ships with the test that validates it, in the same
PR** - the pull request template has a checkbox for exactly that.

| Tier | Project | Scope | Needs Docker | Command |
|---|---|---|---|---|
| Domain unit | `tests/MediFlow.Domain.UnitTests` | Rules, state machines, value objects; pure in-memory | no | `dotnet test tests/MediFlow.Domain.UnitTests` |
| Component (bUnit) | `tests/MediFlow.Blazor.UnitTests` | Presentational Blazor components | no | `dotnet test tests/MediFlow.Blazor.UnitTests` |
| Integration | `tests/MediFlow.IntegrationTests` | Stored procedures, leasing, both API hosts, against real SQL Server | yes | `dotnet test tests/MediFlow.IntegrationTests` |
| End-to-end | `e2e/` | Playwright driving the dashboard, APIs, and worker together | yes | `./scripts/e2e.sh` |

`dotnet test` at the repository root runs the first three together.

## Running each tier

### Domain unit tests

```bash
dotnet test tests/MediFlow.Domain.UnitTests
```

Fast (<a few seconds) and hermetic. CI additionally enforces line coverage on
this project through the coverlet.msbuild package (`-p:CollectCoverage=true
-p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=minimum`) - a change
that drops domain line coverage below 80 % fails the build. The suite currently
measures 80.7 %. The other tiers keep coverlet.collector and are not gated:
their coverage depends on a database or a browser, so a line threshold there
would measure environment wiring more than logic.

### Blazor component tests

```bash
dotnet test tests/MediFlow.Blazor.UnitTests
```

bUnit with AngleSharp rendering; no browser, no server.

### Integration tests

```bash
dotnet test tests/MediFlow.IntegrationTests
```

The suite starts **one SQL Server container per test assembly**
(`TestDatabaseFixture`): Testcontainers pulls the image, then the fixture runs
EF migrations, applies the stored procedures via `SqlScriptRunner`, and seeds
the deterministic demo data - the same bootstrap the enrollment API performs at
startup.

- Image selection: `MEDIFLOW_TEST_SQL_IMAGE` environment variable, defaulting
  to `mcr.microsoft.com/azure-sql-edge:latest` (arm64-native on Apple
  Silicon). CI overrides it to `mcr.microsoft.com/mssql/server:2022-latest`
  for x64 runners.
- All test classes in the `[Collection("database")]` share the one container
  and its seeded data. Tests that need unambiguous rows create their own
  (see the Guid-derived MBIs in `ApiTests.cs`), so no test resets the database.
- Docker must be running. On machines where Testcontainers needs a non-default
  Docker socket (e.g. colima), `~/.testcontainers.properties` points it at the
  right host.

### End-to-end tests

```bash
./scripts/e2e.sh           # headless (default)
./scripts/e2e.sh --headed  # watch it drive a real browser
./scripts/e2e.sh --ui      # interactive Playwright UI mode
```

One command, everything included: the script recreates an isolated
`MediFlow_E2E` database (your dev/demo `MediFlow` database is never touched),
boots all four services against it, waits for health, and runs Playwright.
Requirements: Docker, `sqlcmd` on the PATH, Node 22. First run on a machine
needs the Playwright bits once:

```bash
cd e2e && npm install && npx playwright install
```

Failures leave traces and screenshots in `e2e/test-results/` (config:
`trace: 'retain-on-failure'`, `screenshot: 'only-on-failure'`); CI uploads that
directory as the `playwright-traces` artifact. The config runs a single worker
with `fullyParallel: false` - the specs assume one browser session against one
stack.

## Writing a new test

Copy patterns from the existing suites; the shapes below are the established
conventions.

### A domain unit test (rules engine)

Domain tests are plain xUnit with static builder helpers so each fact reads
like the rule it exercises. From `Claims/AdjudicationTests.cs` - a boundary
test for `CoverageRule`:

```csharp
public class ClaimRuleTests
{
    private static readonly DateOnly ServiceDate = new(2026, 6, 15);

    private static AdjudicationRequest Request(DateOnly? coverageStart = null) =>
        new(
            new Claim
            {
                ClaimNumber = "CLM-2026-000001",
                RenderingProviderNpi = "1234567893",
                ServiceDate = ServiceDate,
                ReceivedAtUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
                Lines = [new ClaimLine { Sequence = 1, ProcedureCode = "99214", ChargeCents = 20000 }],
            },
            new Member { Mbi = "1EG4TE5MK73", FirstName = "A", LastName = "B", StateCode = "TX" },
            new Plan { PlanCode = "MFP-2601", Name = "P", Carrier = "C", Type = PlanType.MedicareAdvantage, ContractYear = 2026, DeductibleCents = 17500, CoinsurancePercent = 20, OopMaxCents = 550000 },
            new EnrollmentApplication
            {
                ApplicationNumber = "ENR-2025-000001", MemberId = 1, PlanId = 1,
                Status = EnrollmentStatus.Active,
                RequestedEffectiveDate = coverageStart ?? new DateOnly(2026, 1, 1),
            },
            new Dictionary<string, ProcedureFee>(),
            new BenefitAccumulator(),
            []);

    [Fact]
    public void Coverage_on_the_service_date_itself_is_included()
    {
        var rule = new CoverageRule();
        Assert.Null(rule.Evaluate(Request(coverageStart: ServiceDate)));  // covered day one
    }
}
```

Rules return a `DenialCode?` - `null` means the rule passes. Keep builder
helpers `static`, pin dates explicitly, and put money in integer cents.

### An integration test

Two ingredients: the `[Collection("database")]` attribute (shares the fixture's
container and seed) and either a service resolved from the fixture or an
in-memory API host from `ApiFactories.cs`.

Testing a stored procedure through the read store (pattern: `StoredProcTests`):

```csharp
[Collection("database")]
public sealed class ClaimAgingTests(TestDatabaseFixture db)
{
    [Fact]
    public async Task Aging_returns_only_open_claims()
    {
        var store = db.Resolve<IReadStore>();
        var page = await store.ClaimsQueueAsync([ClaimStatus.Received], null, null, 1, 10);
        Assert.True(page.Total > 0);
    }
}
```

Testing an HTTP surface (pattern: `EnrollmentApiTests`): the factories build
the real hosts with `Api:Required=true` and `Api:Keys` set to
`integration-test-key`, `Database:InitializeOnStartup=false` and
`Seed:Enabled=false` (the fixture already migrated and seeded), then every
request carries the header:

```csharp
[Collection("database")]
public sealed class ClaimsApiTests(TestDatabaseFixture db) : IDisposable
{
    private readonly ClaimsApiFactory _factory = new(db.ConnectionString);

    [Fact]
    public async Task Missing_api_key_is_unauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/claims/queue");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
```

Conventions: use `db.Resolve<T>()` for DI services, `db.NewDbContext()` for a
fresh `MediFlowDbContext` you may dispose freely, and `_factory.CreateClient()`
for HTTP. The factories target the named entry-point markers
(`EnrollmentApiEntryPoint`, `ClaimsApiEntryPoint`) because the generated
`Program` class is global and ambiguous across the two API projects. Note that
a new or changed `Sql/*.sql` file is an embedded resource - the test run
rebuilds, but an IDE-only "run test without rebuild" can miss it.

### A bUnit component test

Presentational components render with explicit parameters; assert on markup or
invoke callbacks (pattern: `SharedComponentTests`):

```csharp
public class StatusBadgeTests : TestContext
{
    [Fact]
    public void Renders_denied_enrollment_status()
    {
        var cut = RenderComponent<StatusBadge>(parameters => parameters
            .Add(p => p.Kind, "enrollment")
            .Add(p => p.Value, 4));

        cut.MarkupMatches("<span class=\"badge badge-denied\">Denied</span>");
    }
}
```

For callbacks, pass an `EventCallback.Factory.Create<int>(this, …)` capturing
into a local, click the button via `cut.Find(...)`, and assert the captured
value - see `PagerTests.Raises_Go_with_target_page`.

### An E2E spec

Specs live in `e2e/specs/*.spec.ts` and drive the dashboard at
`http://localhost:8090` against the isolated seeded stack. Use
`gotoInteractive` (from `e2e/specs/helpers.ts`) instead of `page.goto`:
Blazor pages prerender on the server before the interactive circuit connects,
and **clicks before the `/_blazor/negotiate` handshake are silently
swallowed** - the helper waits for that response plus a short settle, which is
what keeps the specs deterministic.

```ts
import { expect, test } from '@playwright/test';
import { gotoInteractive } from './helpers';

test('claims queue filters by status', async ({ page }) => {
  await gotoInteractive(page, '/claims');

  await page.getByRole('button', { name: 'Paid', exact: true }).click();
  await expect(page.locator('tbody tr').first()).toContainText('CLM-');
});
```

Seed-aware helpers in the same file find test fixtures through the API
(`firstActiveEnrollment`, `unenrolledEntitledMember`, `first2026MaPlanId`,
`nextMonthFirst`) so specs never hardcode seeded ids.

## CI mapping

Every tier runs in `.github/workflows/ci.yml` on each push and PR:
`build + format` gates everything; `unit-tests`, `integration-tests`, `e2e`,
and a `docker-build` matrix over the four images run after it. Security
scanning of the suites' dependencies lives in `security.yml` (see
`docs/security.md`).
