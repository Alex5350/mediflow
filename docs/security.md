# Security model

MediFlow is a portfolio-scale reference platform, and its security posture is
explicitly two-tier: demo-grade by design locally, with a documented production
path. This page describes what is actually enforced in the code, what the CI
pipeline checks on every change, and what is deliberately out of scope.

## Authentication and authorization

Both REST APIs share one host implementation
(`src/MediFlow.Infrastructure/Web/WebServiceExtensions.cs`), so the security
middleware can never drift between them. Requests are authenticated with an
API key and authorized by nothing finer — the key is the boundary.

`ApiKeyMiddleware` (`src/MediFlow.Infrastructure/Web/ApiKeyMiddleware.cs`):

- Reads the `X-Api-Key` header and compares it against `Api:Keys` — a
  comma-separated list from configuration (`Api__Keys` in environment form).
- Comparison is constant-time: both sides are UTF-8 bytes and the check goes
  through `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals`,
  so an attacker cannot leak key material by timing responses. A length check
  runs first because `FixedTimeEquals` requires equal-length inputs.
- Failures return `401` with an RFC 7807 `application/problem+json` body — no
  detail about *which* part failed.
- A small allowlist stays anonymous so probes and load balancers work without
  credentials: request paths starting with `/health`, `/alive`, `/openapi`, or
  `/scalar` (case-insensitive prefix match). Health monitoring and API docs
  remain reachable; business endpoints do not.
- `Api:Required` defaults to `true`. The only place it is `false` is
  `appsettings.Development.json` in the two API projects, so local runs and the
  E2E stack skip the header while `docker compose` (Production), Kubernetes,
  and Azure all enforce it.

In front of the key check, every response carries baseline hardening headers
(`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer`), and a fixed-window rate limiter is configured
(100 permits per 1-minute window, `QueueLimit = 0`, rejection via `429`) so a
runaway caller cannot hammer the APIs. The Blazor dashboard adds HSTS, a
production exception handler, and antiforgery.

**Keys, where they live.** Configuration only — never code. The committed
`mediflow-dev-key` (in the two `appsettings.json` files and `.env.example`) is
deliberate and documented as local-demo-only: it exists so `git clone &&
./scripts/start.sh` works with zero setup. `docker-compose.yml` reads
`MEDIFLOW_API_KEYS` from `.env` (gitignored; `.env.example` documents the
placeholder). The Azure deployment (`infra/main.bicep`) stores the key set as
Container Apps secrets sourced from a provisioned Key Vault.

**The production path** is Entra ID OIDC: replace `ApiKeyMiddleware` with JWT
bearer authentication and per-scope authorization policies (`claims:read`,
`enrollments:write`, …) — the single `UseMediFlowWebService` composition point
is where that swap happens. The current model is honest about being demo-grade:
one shared static key, no per-consumer identity, no scopes.

## The DevSecOps pipeline

`.github/workflows/security.yml` runs on every push to `main`, every PR, and a
weekly Monday rescan:

| Job | Tool | What it catches |
|---|---|---|
| `codeql` | CodeQL SAST (`security-extended` suite, csharp) | Injection, crypto misuse, and deeper data-flow security bugs than default queries; results as SARIF in the Security tab |
| `snyk` | Snyk SCA + SAST + container | Known-vulnerable NuGet dependencies, static code analysis, and base-image CVEs in the enrollment API image. Gated on the `SNYK_TOKEN` repo secret — the job is a no-op without it so forks and PRs stay green; findings upload as SARIF |
| `secrets` | gitleaks, full git history (`fetch-depth: 0`) | Credentials accidentally committed at any point in history, not just at HEAD |
| `dependency-review` | GitHub dependency-review (PRs only) | New PR dependencies that introduce known vulnerabilities above `high`, and license/scope anomalies versus the base branch |
| `sbom` | anchore `sbom-action`, SPDX JSON | A complete software bill of materials uploaded as a build artifact for audit |

On top of the workflow, `.github/dependabot.yml` opens weekly update PRs for
NuGet (grouped), GitHub Actions, the four Dockerfiles' base images, and the
Playwright npm dependencies — so known-CVE pressure arrives continuously, not
only on the weekly scan. CI (`ci.yml`) separately fails any build on warnings,
which makes NuGet audit data (`NU1902`-class vulnerabilities) a hard error
rather than a log line (see below).

## Secure coding practices in the code

- **Parameterized SQL everywhere.** Reads go through Dapper `CommandDefinition`
  with anonymous-object parameters (`DapperReadStore`); writes go through EF
  Core LINQ or raw `SqlParameter` arrays (`AdjudicationGateway.CommitAdjudicationAsync`,
  including the table-valued parameter). No query builds SQL by string
  concatenation with user input. The one inline SQL statement in the codebase
  (`FailLeaseAsync`) is a constant string whose only variables are declared
  parameters.
- **Secrets via configuration only.** Every secret (connection string, API
  keys) arrives through the .NET configuration stack — `appsettings`,
  environment variables, or orchestrator secrets. `.env.example` carries
  placeholders only; real values are gitignored.
- **Non-root, minimal containers.** All four Dockerfiles create and switch to a
  dedicated `mediflow` user on Alpine base images. The Kubernetes manifests
  (`deploy/k8s`) additionally set `runAsNonRoot: true`,
  `allowPrivilegeEscalation: false`, and `readOnlyRootFilesystem: true`.
- **Warnings as errors, including vulnerability data.**
  `Directory.Build.props` sets `TreatWarningsAsErrors` and
  `AnalysisLevel=latest-recommended`, and `nuget.config` clears all package
  sources except nuget.org so restores are deterministic. Because NuGet's audit
  warnings ship as build warnings, a transitive vulnerability at moderate
  severity or higher (`NU1902`) fails the build outright. This is not
  theoretical: during development it forced upgrades of OpenTelemetry packages
  (pinned to the 1.18.0 line) and AngleSharp (1.7.2 in the bUnit test project)
  before a vulnerable version could land.
- **Least-privilege tool surfaces.** The MCP server (`src/MediFlow.Mcp`) is
  read-only by design; the one stateful operation — adjudication — is exposed
  only as a dry-run preview, so an automation client cannot write to the
  database through it.

## Threat model (short)

| Threat | Mitigation in place | Residual risk |
|---|---|---|
| Spoofed API caller | `X-Api-Key` with constant-time comparison; 401 ProblemDetails; 100/min rate limit; anonymous surface limited to health/docs prefixes | Key is shared and static — no caller identity, rotation, or per-consumer quotas |
| SQL injection | Every query parameterized (Dapper/EF/SqlParameters); no dynamic SQL strings | Negligible by construction; CodeQL and Snyk SAST watch for regressions |
| Data exposure | Demo data only: members, MBIs, and NPIs are synthetically generated (CMS-safe MBI alphabet, valid Luhn NPI check digits); no real beneficiary data exists in the repo or seed | Data is fake, so a leak carries no privacy impact; connection strings still use `TrustServerCertificate=True` locally, acceptable only because the database is localhost demo |
| Supply chain compromise | Package versions pinned in project files; npm lockfile for E2E; Snyk SCA + container, CodeQL, dependency-review, dependabot, SPDX SBOM | NuGet has no lockfile in this repo; the pinned-version + continuous-scan combination is the compensating control |
| Secret leakage | gitleaks over full history; secrets only in configuration; demo key intentionally public and low-value | The committed dev key is a known trade-off for zero-setup demos, not an oversight |

## What is intentionally not production

Be direct about this when presenting the project — the honesty is the point:

- **One static, shared, committed demo key.** No per-caller identity, no
  rotation, no revocation, no scopes. The production replacement is Entra ID
  OIDC with per-scope authorization policies.
- **No per-user authorization.** Any holder of the key can read and write
  everything the API exposes, including enrollment decisions and re-queues.
- **No PII encryption at rest** beyond SQL Server's defaults. Real deployments
  would add Transparent Data Encryption and column-level protection for
  beneficiary identifiers.
- **Placeholder credentials in demo infrastructure.** `deploy/k8s/config.yaml`
  ships `replace-me` secrets (wire ExternalSecrets/CSI for real ones), and the
  Azure SQL firewall rule in `infra/main.bicep` allows Azure-services traffic,
  which should be narrowed for a real environment.
- **Local conveniences that must not leak outward**: `Api:Required=false` in
  Development profiles, `TrustServerCertificate=True` in local connection
  strings, and `mediflow-dev-key` defaults in compose.
