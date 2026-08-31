#!/usr/bin/env bash
# Stops the four demo services (leaves the SQL container running — docker rm -f
# mediflow-sql removes it too when you want that).
set -euo pipefail

for name in api claims worker blazor; do
  pidfile="/tmp/mediflow-$name.pid"
  if [ -f "$pidfile" ]; then
    kill "$(cat "$pidfile")" 2>/dev/null || true
    rm -f "$pidfile"
    echo "stopped $name"
  fi
done
pkill -f "MediFlow.Api" 2>/dev/null || true
pkill -f "MediFlow.Claims.Api" 2>/dev/null || true
pkill -f "MediFlow.Worker" 2>/dev/null || true
pkill -f "MediFlow.Blazor" 2>/dev/null || true
echo "done"
