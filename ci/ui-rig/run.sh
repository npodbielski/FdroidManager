#!/usr/bin/env bash
# Builds the browser rig from wwwroot, serves it, runs the Lightpanda checks.
# Exit status is the checks' exit status.
#
#   ci/ui-rig/run.sh                      # needs `lightpanda` on PATH
#   LIGHTPANDA=~/bin/lightpanda run.sh    # or point at the binary
#
# Environment: PORT (default 8899), RIG_DIR (keep the built rig there for
# inspection instead of a throwaway directory).
set -euo pipefail
cd "$(dirname "$0")"

PORT="${PORT:-8899}"
LIGHTPANDA="${LIGHTPANDA:-lightpanda}"
# RIG_DIR keeps a build for inspection; otherwise it is throwaway.
RIG_DIR_KEEP="${RIG_DIR:-}"
RIG_DIR="${RIG_DIR_KEEP:-$(mktemp -d "${TMPDIR:-/tmp}/fdroidmgr-ui-rig.XXXXXX")}"

# Fail here rather than run the checks against whatever else is on the port.
if ! node -e "require('net').createServer().listen($PORT, '127.0.0.1', function () { this.close() })" 2>/dev/null; then
  echo "port $PORT is in use; the checks would test that other server (PORT=… to move)" >&2
  exit 1
fi

node build.mjs "$RIG_DIR"
node serve.mjs "$RIG_DIR" "$PORT" >/dev/null &
server=$!
cleanup() {
  kill "$server" 2>/dev/null || true
  if [ -z "$RIG_DIR_KEEP" ]; then
    rm -rf "$RIG_DIR"
  fi
}
trap cleanup EXIT

# Lightpanda substitutes $LP_UI_RIG_BASE in the string arguments of page calls.
export LP_UI_RIG_BASE="http://127.0.0.1:$PORT"

for attempt in $(seq 1 100); do
  if node -e "fetch(process.env.LP_UI_RIG_BASE + '/ui.html').then(r => process.exit(r.ok ? 0 : 1)).catch(() => process.exit(1))"; then
    break
  fi
  if [ "$attempt" = 100 ]; then
    echo "rig server did not come up on $PORT" >&2
    exit 1
  fi
  sleep 0.1
done

"$LIGHTPANDA" run --log-level warn --log-filter -note check.js
