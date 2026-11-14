#!/usr/bin/env bash
# Starts the browser demo: the manager plus a cookie-less demo identity
# provider, both behind one origin (see README.md in this directory).
#
#   http://<DEMO_IP>:<DEMO_FRONT_PORT>/ui.html     the UI, sign in as alice / pw
#
# Everything runs from demo/work (gitignored) and is wiped on each start.
set -euo pipefail

DEMO_DIR=$(cd "$(dirname "$0")" && pwd)
REPO_ROOT=$(cd "$DEMO_DIR/.." && pwd)
WORK=${DEMO_WORK:-$DEMO_DIR/work}
DEMO_IP=${DEMO_IP:-127.0.0.1}       # a LAN address when browsing from another device
FRONT_PORT=${DEMO_FRONT_PORT:-8090} # the single origin the browser talks to
APP_PORT=${DEMO_APP_PORT:-8091}
IDP_PORT=${DEMO_IDP_PORT:-8084}

command -v dotnet >/dev/null || { echo "dotnet SDK is required" >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 is required" >&2; exit 1; }
python3 -c "import cryptography" 2>/dev/null || { echo "python3 cryptography is required (pip install cryptography)" >&2; exit 1; }

bash "$DEMO_DIR/stop-demo.sh" >/dev/null

rm -rf "$WORK"
mkdir -p "$WORK"/{repo/repo,data,dp,config,keys,logs}

# 1. Publish the API; the UI and the generated CSS live in its wwwroot.
dotnet publish "$REPO_ROOT/src/FdroidMgr.Api/FdroidMgr.Api.csproj" \
  -c Release -o "$WORK/publish" --nologo -v q

# 2. The demo IdP. Bound to 127.0.0.1; it is only reached through the front,
#    so the browser never sees a second origin for it.
setsid nohup python3 "$DEMO_DIR/idp.py" "http://$DEMO_IP:$FRONT_PORT/_idp" "$IDP_PORT" alice:pw \
  > "$WORK/logs/idp.log" 2>&1 < /dev/null &
echo $! > "$WORK/idp.pid"

# 3. The manager. The config file starts as {} so the anonymous OIDC
#    bootstrap endpoint (step 5) is open. The repo has no config.yml yet:
#    the manager's startup index rebuild only runs on an initialized repo,
#    and it would replace the fixture with an empty index.
printf '{}\n' > "$WORK/config/appsettings.json"
cd "$WORK/publish"
setsid nohup env \
  ASPNETCORE_URLS="http://$DEMO_IP:$APP_PORT" \
  FDROID_CONFIG_FILE="$WORK/config/appsettings.json" \
  FDROID_DB="Data Source=$WORK/data/fdroidmgr.db" \
  FDROID_DATAPROTECTION_PATH="$WORK/dp" \
  FDROID_MASTER_KEY=0123456789abcdef0123456789abcdef0123456789abcdef \
  FDROID_REPO_ROOT="$WORK/repo" FDROID_REPO_PATH="$WORK/repo" FDROID_KEY_PATH="$WORK/keys" \
  FDROID_REPO_NAME="Demo repo" FDROID_REPO_URL="http://$DEMO_IP:$FRONT_PORT/fdroid/repo" \
  dotnet FdroidMgr.Api.dll > "$WORK/logs/app.log" 2>&1 < /dev/null &
echo $! > "$WORK/app.pid"

for _ in $(seq 1 60); do
  curl -fs -o /dev/null "http://127.0.0.1:$APP_PORT/ui.html" 2>/dev/null && break
  sleep 1
done

# 4. The single-origin front.
setsid nohup python3 "$DEMO_DIR/proxy.py" "$FRONT_PORT" "$IDP_PORT" "http://$DEMO_IP:$APP_PORT" \
  > "$WORK/logs/proxy.log" 2>&1 < /dev/null &
echo $! > "$WORK/proxy.pid"
sleep 2

# 5. Configure the OIDC provider through the anonymous bootstrap endpoint,
#    open only while none is stored.
curl -s -o /dev/null -w 'provider configured: %{http_code}\n' -X POST "http://127.0.0.1:$FRONT_PORT/api/auth/oauth" \
  -H 'content-type: application/json' \
  -d "{\"authority\":\"http://$DEMO_IP:$FRONT_PORT/_idp\",\"clientId\":\"fdroidmgr\",\"clientSecret\":\"test-secret\",\"scopes\":[\"openid\",\"profile\",\"email\"],\"allowInsecure\":true}"
sleep 1

# 6. Seed the demo repo (config.yml + the hand-written index fixture + the
#    database), after startup so the startup rebuild stays away from it.
python3 "$DEMO_DIR/seed-repo.py" "$WORK" "$DEMO_IP" "$FRONT_PORT"

echo
echo "demo on  http://$DEMO_IP:$FRONT_PORT/ui.html  (sign in: alice / pw)"
echo "logs in   $WORK/logs/{app,idp,proxy}.log"
echo "stop with $DEMO_DIR/stop-demo.sh"
