#!/usr/bin/env bash
# Self-bootstrapping E2E run: isolated database, all four services, Playwright.
# Dev data is never touched — the suite runs against MediFlow_E2E.
#
#   ./scripts/e2e.sh              # headless (default)
#   ./scripts/e2e.sh --headed     # watch it drive a real browser
#   ./scripts/e2e.sh --ui         # interactive Playwright UI
set -euo pipefail
cd "$(dirname "$0")/.."

E2E_DB="MediFlow_E2E"
SQL_CONTAINER="mediflow-sql"
SQL_PORT="${SQL_PORT:-1433}"
MODE="${1:---}"

echo "==> [1/5] SQL Server"
if ! docker inspect "$SQL_CONTAINER" >/dev/null 2>&1; then
  echo "    starting azure-sql-edge (arm64-friendly; set SQL_IMAGE to override)"
  docker run -d --name "$SQL_CONTAINER" \
    -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=MediFlow!Dev1" \
    -p "$SQL_PORT:1433" "${SQL_IMAGE:-mcr.microsoft.com/azure-sql-edge:latest}" >/dev/null
  sleep 20
fi

# Isolated E2E database (separate from dev/demo data). sqlcmd resets it when
# available (local reruns); on clean machines the enrollment API's migrator
# creates it on first boot.
echo "==> [2/5] fresh $E2E_DB database"
export ConnectionStrings__MediFlowDb="Server=localhost,$SQL_PORT;Database=$E2E_DB;User ID=sa;Password=MediFlow!Dev1;TrustServerCertificate=True;Encrypt=True"
if command -v sqlcmd >/dev/null 2>&1; then
  sqlcmd -S "localhost,$SQL_PORT" -U sa -P 'MediFlow!Dev1' \
    -Q "IF DB_ID(N'$E2E_DB') IS NOT NULL DROP DATABASE [$E2E_DB]; CREATE DATABASE [$E2E_DB]" >/dev/null
else
  echo "    sqlcmd not found — relying on EF migration to create $E2E_DB"
fi
export Seed__Enabled=true
export ASPNETCORE_ENVIRONMENT=Development
# Only the Enrollment API bootstraps (migrate/seed); the other services must
# not race it on CREATE/INSERT during a shared-database boot.
export Database__InitializeOnStartup=true

echo "==> [3/5] building"
dotnet build -v quiet 2>/dev/null

echo "==> [4/5] services (api 8080, claims 8081, worker, blazor 8090)"
dotnet run --project src/MediFlow.Api --no-build > /tmp/mediflow-e2e-api.log 2>&1 &
API_PID=$!
Database__InitializeOnStartup=false Seed__Enabled=false dotnet run --project src/MediFlow.Claims.Api --no-build > /tmp/mediflow-e2e-claims.log 2>&1 &
CLAIMS_PID=$!
Database__InitializeOnStartup=false Seed__Enabled=false dotnet run --project src/MediFlow.Worker --no-build > /tmp/mediflow-e2e-worker.log 2>&1 &
WORKER_PID=$!
Seed__Enabled=false dotnet run --project src/MediFlow.Blazor --no-build > /tmp/mediflow-e2e-blazor.log 2>&1 &
BLAZOR_PID=$!

cleanup() {
  kill "$API_PID" "$CLAIMS_PID" "$WORKER_PID" "$BLAZOR_PID" 2>/dev/null || true
}
trap cleanup EXIT

for url in "http://localhost:8080/health/live" "http://localhost:8081/health/live" "http://localhost:8090/health/live"; do
  for _ in $(seq 1 90); do
    if curl -sf -o /dev/null "$url"; then break; fi
    sleep 2
  done
done
echo "    all services healthy (seeded: deterministic demo data)"

echo "==> [5/5] Playwright ($MODE)"
cd e2e
# One-command guarantee: dependencies + browser on a clean machine.
npm ci --silent
npx playwright install --with-deps chromium
case "$MODE" in
  --headed) npx playwright test --headed ;;
  --ui)     npx playwright test --ui ;;
  *)        npx playwright test ;;
esac
