#!/usr/bin/env bash
# Stops the browser demo. Ports, not process patterns: a pattern would also
# match the shell that runs this script.
DEMO_DIR=$(cd "$(dirname "$0")" && pwd)
WORK=${DEMO_WORK:-$DEMO_DIR/work}
FRONT_PORT=${DEMO_FRONT_PORT:-8090}
APP_PORT=${DEMO_APP_PORT:-8091}
IDP_PORT=${DEMO_IDP_PORT:-8084}

for port in "$FRONT_PORT" "$APP_PORT" "$IDP_PORT"; do
  for pid in $(ss -lptHn "sport = :$port" 2>/dev/null | grep -oP 'pid=\K[0-9]+' | sort -u); do
    kill "$pid" 2>/dev/null && echo "stopped :$port (pid $pid)"
  done
done
rm -f "$WORK"/*.pid 2>/dev/null
sleep 1
echo "demo stopped (logs: $WORK/logs/)"
