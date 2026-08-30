#!/usr/bin/env bash
# Local demo: SQL container (fresh demo database) + all four services.
# Leaves the processes in the background; ./scripts/stop.sh tears them down.
set -euo pipefail
cd "$(dirname "$0")/.."

SQL_CONTAINER="mediflow-sql"
SQL_PORT="${SQL_PORT:-1433}"

if ! docker inspect "$SQL_CONTAINER" >/dev/null 2>&1; then
  echo "==> starting SQL Server container (azure-sql-edge)"
  docker run -d --name "$SQL_CONTAINER" \
    -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=MediFlow!Dev1" \
    -p "$SQL_PORT:1433" "${SQL_IMAGE:-mcr.microsoft.com/azure-sql-edge:latest}" >/dev/null
  sleep 20
fi

export ConnectionStrings__MediFlowDb="Server=localhost,$SQL_PORT;Database=MediFlow;User ID=sa;Password=MediFlow!Dev1;TrustServerCertificate=True;Encrypt=True"
export Seed__Enabled=true          # deterministic demo data (skipped if db non-empty)
export Seed__Reset="${SEED_RESET:-false}"
export ASPNETCORE_ENVIRONMENT=Development

echo "==> building"
dotnet build -v quiet 2>/dev/null

echo "==> starting services"
dotnet run --project src/MediFlow.Api --no-build > /tmp/mediflow-api.log 2>&1 &
echo $! > /tmp/mediflow-api.pid
Database__InitializeOnStartup=false Seed__Enabled=false dotnet run --project src/MediFlow.Claims.Api --no-build > /tmp/mediflow-claims.log 2>&1 &
echo $! > /tmp/mediflow-claims.pid
Database__InitializeOnStartup=false Seed__Enabled=false dotnet run --project src/MediFlow.Worker --no-build > /tmp/mediflow-worker.log 2>&1 &
echo $! > /tmp/mediflow-worker.pid
Seed__Enabled=false dotnet run --project src/MediFlow.Blazor --no-build > /tmp/mediflow-blazor.log 2>&1 &
echo $! > /tmp/mediflow-blazor.pid

for url in "http://localhost:8080/health/live" "http://localhost:8081/health/live" "http://localhost:8090/health/live"; do
  for _ in $(seq 1 90); do
    if curl -sf -o /dev/null "$url"; then break; fi
    sleep 2
  done
done

echo
echo "MediFlow is up:"
echo "  Dashboard    http://localhost:8090"
echo "  Enrollment API  http://localhost:8080/scalar/v1  (X-Api-Key not required in Development)"
echo "  Claims API      http://localhost:8081/scalar/v1"
echo "  MCP server      ./scripts/mcp-smoke.sh"
echo "Logs in /tmp/mediflow-*.log — stop with ./scripts/stop.sh"
