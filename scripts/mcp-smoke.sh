#!/usr/bin/env bash
# Smoke test for the MediFlow MCP server: drives the stdio JSON-RPC protocol
# end to end — initialize, tools/list, and two tools/call invocations.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet build src/MediFlow.Mcp -v quiet 2>/dev/null
MCP_BIN=$(ls src/MediFlow.Mcp/bin/Debug/net10.0/MediFlow.Mcp)

# Keep stdin open briefly after sending — the server shuts down on EOF before
# responses flush otherwise. (No `timeout` on macOS; the held stdin bounds runtime.)
OUT=$((cat; sleep 8) <<'MESSAGES' | "$MCP_BIN" 2>/dev/null || true
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"explain_denial_code","arguments":{"code":"CO-18"}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"claims_queue","arguments":{"statuses":"Received,Adjudicating","page":1}}}
MESSAGES
)

echo "$OUT" | grep -q '"serverInfo"' && echo "PASS initialize" || { echo "FAIL initialize"; echo "$OUT" | head -5; exit 1; }
TOOLS=$(echo "$OUT" | grep '"id":2' | grep -o '"name":"[a-z_0-9]*"' | wc -l | tr -d ' ')
echo "PASS tools/list ($TOOLS tools advertised)"
echo "$OUT" | grep -q 'duplicate' && echo "PASS explain_denial_code" || { echo "FAIL explain_denial_code"; exit 1; }
echo "$OUT" | grep -q 'total' && echo "PASS claims_queue" || { echo "FAIL claims_queue"; exit 1; }
