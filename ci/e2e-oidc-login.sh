#!/usr/bin/env bash
# E2E test: interactive OIDC login against a tiny IdP (oidc-server-mock).
# Pure curl + a shared cookie jar — no browser (docs/oauth-login.md §10).
#
#   1. GET  /api/auth/login        → 302 to the IdP authorize URL (PKCE+nonce)
#   2. GET  authorize              → 302 to the IdP login page
#   3. GET  login page             → extract __RequestVerificationToken + ReturnUrl
#   4. POST Account/Login           → 302 to the IdP authorize callback
#   5. GET  authorize callback      → 302 to /signin-oidc?code=...&state=...
#   6. GET  /signin-oidc            → session cookie (.FdroidMgr.Auth)
#   7. GET  /api/auth/me            → 200 {"username":"alice","roles":[]}
#
# Usage:
#   bash ci/e2e-oidc-login.sh [BASE_URL] [IDP_URL] [USERNAME] [PASSWORD]
#
# Requires:
#   - the manager running at BASE_URL (already configured via POST /api/auth/oauth
#     and restarted), with its OAuthIssuer pointing at IDP_URL
#   - oidc-server-mock running at IDP_URL (ci/oidc/clients.json + users.json)
#   - curl only (no jq)

set -euo pipefail

BASE_URL="${1:-http://localhost:8082}"
IDP_URL="${2:-http://localhost:8084}"
OIDC_USER="${3:-alice}"
OIDC_PASS="${4:-pw}"

JAR="$(mktemp)"
trap 'rm -f "$JAR"' EXIT

# First value of a response header (case-insensitive name).
hdr() { awk -v name="$1" 'tolower($1)==tolower(name":"){sub(/\r$/,"",$2); print $2}' ; }

echo "=== E2E Test: OIDC interactive login ==="
echo "Manager: $BASE_URL"
echo "IdP:     $IDP_URL"
echo ""

echo "Step 1: challenge → IdP authorize URL"
AUTHZ=$(curl -fsS -c "$JAR" -D - -o /dev/null "$BASE_URL/api/auth/login" | hdr Location)
if [ -z "$AUTHZ" ]; then
  echo "❌ no redirect from /api/auth/login"
  exit 1
fi
echo "✓ authorize URL issued"
echo ""

echo "Step 2: follow authorize → login page"
LOGIN_PAGE=$(curl -fsS -b "$JAR" -c "$JAR" -D - -o /dev/null "$AUTHZ" | hdr Location)
if [ -z "$LOGIN_PAGE" ] || ! printf '%s' "$LOGIN_PAGE" | grep -q "/Account/Login"; then
  echo "❌ expected a redirect to the IdP login page, got: $LOGIN_PAGE"
  exit 1
fi
echo "✓ login page located"
echo ""

echo "Step 3: fetch login form (anti-forgery token + ReturnUrl)"
LOGIN_HTML=$(curl -fsS -b "$JAR" -c "$JAR" "$LOGIN_PAGE")
TOKEN=$(printf '%s' "$LOGIN_HTML" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | sed 's/.*value="//; s/"$//')
RETURNURL=$(printf '%s' "$LOGIN_HTML" | grep -o 'name="ReturnUrl" value="[^"]*"' | sed 's/.*value="//; s/"$//' | sed 's/&amp;/\&/g')
if [ -z "$TOKEN" ] || [ -z "$RETURNURL" ]; then
  echo "❌ could not extract login form fields"
  exit 1
fi
echo "✓ form fields extracted"
echo ""

echo "Step 4: submit login form ($OIDC_USER)"
AUTH_CB=$(curl -fsS -b "$JAR" -c "$JAR" -D - -o /dev/null -X POST "$IDP_URL/Account/Login" \
  --data-urlencode "Username=$OIDC_USER" \
  --data-urlencode "Password=$OIDC_PASS" \
  --data-urlencode "RememberLogin=false" \
  --data-urlencode "button=login" \
  --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "ReturnUrl=$RETURNURL" \
  | hdr Location)
if [ -z "$AUTH_CB" ] || ! printf '%s' "$AUTH_CB" | grep -q "connect/authorize/callback"; then
  echo "❌ login was not accepted (bad credentials or form mismatch): $AUTH_CB"
  exit 1
fi
echo "✓ login accepted"
echo ""

echo "Step 5: authorize callback → /signin-oidc?code=...&state=..."
SIGNIN=$(curl -fsS -b "$JAR" -c "$JAR" -D - -o /dev/null "$IDP_URL$AUTH_CB" | hdr Location)
if [ -z "$SIGNIN" ] || ! printf '%s' "$SIGNIN" | grep -q "/signin-oidc?code="; then
  echo "❌ expected a redirect to /signin-oidc with a code, got: $SIGNIN"
  exit 1
fi
echo "✓ code returned"
echo ""

echo "Step 6: callback → session cookie"
HDRS=$(curl -fsS -b "$JAR" -c "$JAR" -D - -o /dev/null "$SIGNIN")
if ! printf '%s' "$HDRS" | grep -qi "Set-Cookie: .FdroidMgr.Auth="; then
  echo "❌ no session cookie set"
  echo "$HDRS" | grep -i "set-cookie" || true
  exit 1
fi
echo "✓ session cookie set"
echo ""

echo "Step 7: authenticated /api/auth/me"
ME=$(curl -fsS -b "$JAR" "$BASE_URL/api/auth/me")
echo "$ME"
if ! printf '%s' "$ME" | grep -q "\"username\":[\"']\?$OIDC_USER"; then
  echo "❌ /api/auth/me did not return the IdP username"
  exit 1
fi
echo "✓ authenticated as $OIDC_USER"
echo ""

echo "=== OIDC login E2E Complete ==="
