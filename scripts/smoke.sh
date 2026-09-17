#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BASE="${BASE_URL:-https://localhost:7071}"
MCP="$BASE/mcp"
PROTOCOL="2026-07-28"
REDIRECT_URI="http://127.0.0.1:9999/callback"
CLIENT_ID="cursor-mcp-spike"
FAILED=0
STARTED_SERVER=0
SERVER_PID=""

log() { printf '\n==> %s\n' "$1"; }
pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; FAILED=1; }

PYTHON="${PYTHON:-python}"
command -v "$PYTHON" >/dev/null 2>&1 || PYTHON=python3

urlencode() {
  "$PYTHON" -c 'import urllib.parse,sys; sys.stdout.write(urllib.parse.quote(sys.argv[1], safe=""))' "$1"
}

json_get() {
  "$PYTHON" - "$1" "$2" <<'PY'
import json, sys
path, raw = sys.argv[1], sys.argv[2]
data = json.loads(raw)
for key in path.split("."):
    if key.isdigit():
        data = data[int(key)]
    else:
        data = data.get(key) if isinstance(data, dict) else data[int(key)]
if data is None:
    raise SystemExit("missing " + path)
if isinstance(data, (dict, list)):
    print(json.dumps(data))
else:
    print(data)
PY
}

mcp_meta() {
  cat <<EOF
{
  "io.modelcontextprotocol/protocolVersion": "$PROTOCOL",
  "io.modelcontextprotocol/clientInfo": { "name": "cursor-mcp-smoke", "version": "1.0.0" },
  "io.modelcontextprotocol/clientCapabilities": {}
}
EOF
}

parse_mcp_body() {
  "$PYTHON" - "$1" <<'PY'
import json, sys
raw = open(sys.argv[1], "r", encoding="utf-8").read().strip()
if not raw:
    print("{}")
    raise SystemExit
if raw.startswith("event:") or raw.startswith("data:"):
    for line in raw.splitlines():
        if line.startswith("data:"):
            print(line[5:].strip())
            raise SystemExit
    print("{}")
    raise SystemExit
print(raw)
PY
}

wait_for_health() {
  for _ in $(seq 1 40); do
    if curl -skf "$BASE/health" >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.5
  done
  return 1
}

cleanup() {
  if [[ "$STARTED_SERVER" == "1" && -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

if ! curl -skf "$BASE/health" >/dev/null 2>&1; then
  log "Starting HTTPS server"
  dotnet run --project "$ROOT/src/CursorMcp.Server/CursorMcp.Server.csproj" --launch-profile https --configuration Release >/tmp/cursor-mcp-spike.log 2>&1 &
  SERVER_PID=$!
  STARTED_SERVER=1
  if ! wait_for_health; then
    echo "Server failed to start. Log:"
    cat /tmp/cursor-mcp-spike.log || true
    exit 1
  fi
fi

log "Health and Scalar"
HEALTH="$(curl -sk "$BASE/health")"
echo "$HEALTH" | grep -q '"status":"ok"' && pass "GET /health" || fail "GET /health: $HEALTH"
SCALAR_CODE="$(curl -skL -o /tmp/scalar.html -w '%{http_code}' "$BASE/scalar")"
[[ "$SCALAR_CODE" == "200" ]] && grep -qi 'scalar\|api reference\|openapi' /tmp/scalar.html && pass "GET /scalar" || fail "GET /scalar ($SCALAR_CODE)"
OPENAPI_CODE="$(curl -sk -o /tmp/openapi.json -w '%{http_code}' "$BASE/openapi/v1.json")"
[[ "$OPENAPI_CODE" == "200" ]] && grep -q 'Cursor MCP Spike' /tmp/openapi.json && pass "GET /openapi/v1.json" || fail "GET /openapi/v1.json ($OPENAPI_CODE)"

log "OAuth discovery"
PRM="$(curl -sk "$BASE/.well-known/oauth-protected-resource")"
echo "$PRM" | grep -q 'authorization_servers' && echo "$PRM" | grep -q "$MCP" && pass "Protected resource metadata" || fail "PRM: $PRM"
ASM="$(curl -sk "$BASE/.well-known/oauth-authorization-server")"
echo "$ASM" | grep -F -q '"code_challenge_methods_supported":["S256"]' && echo "$ASM" | grep -F -q '"authorization_endpoint"' && echo "$ASM" | grep -F -q '"refresh_token"' && pass "Authorization server metadata" || fail "AS metadata: $ASM"
echo "$ASM" | grep -q '"registration_endpoint"' && fail "Metadata should not advertise DCR" || pass "Metadata omits registration_endpoint"

log "Pre-registered client validation"
DUMMY_CHALLENGE="E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
UNKNOWN_CLIENT="$(curl -sk "$BASE/authorize?response_type=code&client_id=unknown-app&redirect_uri=$(urlencode "$REDIRECT_URI")&state=x&code_challenge=$DUMMY_CHALLENGE&code_challenge_method=S256")"
echo "$UNKNOWN_CLIENT" | grep -q 'unauthorized_client' && pass "Unknown client_id is rejected" || fail "Unknown client: $UNKNOWN_CLIENT"
BAD_URI="$(curl -sk "$BASE/authorize?response_type=code&client_id=$(urlencode "$CLIENT_ID")&redirect_uri=$(urlencode "https://evil.example/callback")&state=x&code_challenge=$DUMMY_CHALLENGE&code_challenge_method=S256")"
echo "$BAD_URI" | grep -q 'redirect_uri is not registered' && pass "Unregistered redirect_uri is rejected" || fail "Bad redirect: $BAD_URI"
UNKNOWN_TOKEN="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "client_id=unknown-app" \
  --data-urlencode "code=not-a-code" \
  --data-urlencode "redirect_uri=$REDIRECT_URI" \
  --data-urlencode "code_verifier=not-a-verifier")"
echo "$UNKNOWN_TOKEN" | grep -q 'invalid_client' && pass "Unknown client_id on /token is rejected" || fail "Token unknown client: $UNKNOWN_TOKEN"

log "401 challenge without bearer token"
CHALLENGE_HEADERS="$(curl -sk -D - -o /tmp/mcp_unauth.json -X POST "$MCP" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "MCP-Protocol-Version: $PROTOCOL" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"server/discover\",\"params\":{\"_meta\":$(mcp_meta)}}")"
echo "$CHALLENGE_HEADERS" | grep -q 'HTTP/.* 401' || fail "Expected 401 for unauthenticated /mcp"
echo "$CHALLENGE_HEADERS" | grep -i 'www-authenticate' | grep -qi "resource_metadata=\"$BASE/.well-known/oauth-protected-resource\"" \
  && pass "WWW-Authenticate resource_metadata" \
  || fail "Missing resource_metadata challenge: $CHALLENGE_HEADERS"

log "Authorization code + PKCE + login + token"
read -r VERIFIER CHALLENGE < <("$PYTHON" - <<'PY'
import os, hashlib, base64, sys
verifier = base64.urlsafe_b64encode(os.urandom(32)).rstrip(b"=").decode()
challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
sys.stdout.write(f"{verifier} {challenge}\n")
PY
)
VERIFIER="${VERIFIER//$'\r'/}"
CHALLENGE="${CHALLENGE//$'\r'/}"

AUTH_URL="$BASE/authorize?response_type=code&client_id=$(urlencode "$CLIENT_ID")&redirect_uri=$(urlencode "$REDIRECT_URI")&state=smoke-state&code_challenge=$(urlencode "$CHALLENGE")&code_challenge_method=S256&scope=$(urlencode mcp:tools)&resource=$(urlencode "$MCP")"
AUTH_HEADERS="$(curl -sk -D - -o /dev/null "$AUTH_URL")"
LOGIN_URL="$(echo "$AUTH_HEADERS" | awk 'tolower($1)=="location:"{print $2}' | tr -d '\r' | tail -n1)"
[[ "$LOGIN_URL" == *"/login?ticket="* ]] && pass "GET /authorize -> /login" || fail "Authorize redirect: $AUTH_HEADERS"
[[ "$LOGIN_URL" == http* ]] || LOGIN_URL="$BASE${LOGIN_URL}"
TICKET="${LOGIN_URL##*ticket=}"
LOGIN_PAGE="$(curl -sk "$LOGIN_URL")"
echo "$LOGIN_PAGE" | grep -q 'Entrar como demo-user' && pass "GET /login renders button" || fail "Login page missing button"
echo "$LOGIN_PAGE" | grep -q 'cursor-mcp-spike' && pass "GET /login shows registered client_id" || fail "Login page missing client_id"

LOGIN_HEADERS="$(curl -sk -D - -o /dev/null -X POST "$BASE/login" -H "Content-Type: application/x-www-form-urlencoded" --data-urlencode "ticket=$TICKET")"
CALLBACK="$(echo "$LOGIN_HEADERS" | awk 'tolower($1)=="location:"{print $2}' | tr -d '\r' | tail -n1)"
echo "$CALLBACK" | grep -q 'code=' && echo "$CALLBACK" | grep -q 'state=smoke-state' && pass "POST /login callback to Cursor redirect_uri" || fail "Login callback: $LOGIN_HEADERS"
CODE="$("$PYTHON" - "$CALLBACK" <<'PY'
import sys, urllib.parse
qs = urllib.parse.parse_qs(urllib.parse.urlparse(sys.argv[1]).query)
print((qs.get("code") or [""])[0])
PY
)"
[[ -n "$CODE" ]] || fail "Authorization code missing from callback"

TOKEN_JSON="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "code=$CODE" \
  --data-urlencode "redirect_uri=$REDIRECT_URI" \
  --data-urlencode "client_id=$CLIENT_ID" \
  --data-urlencode "code_verifier=$VERIFIER" \
  --data-urlencode "resource=$MCP")"
echo "$TOKEN_JSON" | grep -F -q '"token_type":"Bearer"' && pass "POST /token issues JWT" || fail "Token response: $TOKEN_JSON"
ACCESS_TOKEN="$(json_get access_token "$TOKEN_JSON" 2>/dev/null || true)"
REFRESH_TOKEN="$(json_get refresh_token "$TOKEN_JSON" 2>/dev/null || true)"
[[ "${#ACCESS_TOKEN}" -gt 20 ]] && pass "JWT length" || fail "JWT missing: $TOKEN_JSON"
[[ "${#REFRESH_TOKEN}" -gt 20 ]] && pass "POST /token issues refresh_token" || fail "Refresh missing: $TOKEN_JSON"
if [[ "${#ACCESS_TOKEN}" -gt 20 ]]; then
"$PYTHON" - "$ACCESS_TOKEN" <<'PY'
import json, sys, base64
token = sys.argv[1]
payload = token.split(".")[1] + "=" * ((4 - len(token.split(".")[1]) % 4) % 4)
data = json.loads(base64.urlsafe_b64decode(payload.encode()))
assert data.get("sub") == "demo-user", data
assert "mcp:tools" in data.get("scope", ""), data
assert data.get("client_id") == "cursor-mcp-spike", data
print("ok")
PY
pass "JWT claims include sub, client_id, and scope"
fi

log "Authenticated MCP hello world"
mcp_call() {
  local method="$1"
  local extra="$2"
  local name_header="${3:-}"
  local headers=(-H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -H "MCP-Protocol-Version: $PROTOCOL" -H "Mcp-Method: $method")
  if [[ -n "$name_header" ]]; then
    headers+=(-H "Mcp-Name: $name_header")
  fi
  curl -sk "${headers[@]}" -X POST "$MCP" -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"$method\",\"params\":{\"_meta\":$(mcp_meta)${extra}}}" -o /tmp/mcp_body.txt
  parse_mcp_body /tmp/mcp_body.txt
}

DISCOVER="$(mcp_call "server/discover" "")"
echo "$DISCOVER" | grep -q '2026-07-28' && pass "server/discover" || fail "discover: $DISCOVER"

TOOLS="$(mcp_call "tools/list" "")"
echo "$TOOLS" | grep -q 'hello_world' && pass "tools/list contains hello_world" || fail "tools/list: $TOOLS"

CALL="$(mcp_call "tools/call" ",\"name\":\"hello_world\",\"arguments\":{}" "hello_world")"
echo "$CALL" | grep -q 'Hello, world!' && pass "tools/call hello_world" || fail "tools/call: $CALL"

RESOURCES="$(mcp_call "resources/list" "")"
echo "$RESOURCES" | grep -q 'hello://world' && pass "resources/list contains hello://world" || fail "resources/list: $RESOURCES"

READ="$(mcp_call "resources/read" ",\"uri\":\"hello://world\"" "hello://world")"
echo "$READ" | grep -q 'Hello, world!' && pass "resources/read hello://world" || fail "resources/read: $READ"

log "Refresh token rotation"
REFRESH_JSON="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=refresh_token" \
  --data-urlencode "refresh_token=$REFRESH_TOKEN" \
  --data-urlencode "client_id=$CLIENT_ID" \
  --data-urlencode "resource=$MCP")"
echo "$REFRESH_JSON" | grep -F -q '"token_type":"Bearer"' && pass "POST /token refresh_token issues JWT" || fail "Refresh response: $REFRESH_JSON"
ROTATED_ACCESS="$(json_get access_token "$REFRESH_JSON" 2>/dev/null || true)"
ROTATED_REFRESH="$(json_get refresh_token "$REFRESH_JSON" 2>/dev/null || true)"
[[ "${#ROTATED_ACCESS}" -gt 20 ]] && [[ "$ROTATED_ACCESS" != "$ACCESS_TOKEN" ]] && pass "Refresh issues a new access token" || fail "Rotated access missing: $REFRESH_JSON"
[[ "${#ROTATED_REFRESH}" -gt 20 ]] && [[ "$ROTATED_REFRESH" != "$REFRESH_TOKEN" ]] && pass "Refresh token is rotated" || fail "Rotated refresh missing: $REFRESH_JSON"

REUSE_REFRESH="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=refresh_token" \
  --data-urlencode "refresh_token=$REFRESH_TOKEN" \
  --data-urlencode "client_id=$CLIENT_ID")"
echo "$REUSE_REFRESH" | grep -q 'invalid_grant' && pass "Reused refresh token is rejected" || fail "Reused refresh: $REUSE_REFRESH"

REVOKED_FAMILY="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=refresh_token" \
  --data-urlencode "refresh_token=$ROTATED_REFRESH" \
  --data-urlencode "client_id=$CLIENT_ID")"
echo "$REVOKED_FAMILY" | grep -q 'invalid_grant' && pass "Refresh family is revoked after reuse" || fail "Family after reuse: $REVOKED_FAMILY"

log "Negative cases"
REUSE="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "code=$CODE" \
  --data-urlencode "redirect_uri=$REDIRECT_URI" \
  --data-urlencode "client_id=$CLIENT_ID" \
  --data-urlencode "code_verifier=$VERIFIER" \
  --data-urlencode "resource=$MCP")"
echo "$REUSE" | grep -q 'invalid_grant' && pass "Reused authorization code is rejected" || fail "Reused code: $REUSE"

read -r VERIFIER2 CHALLENGE2 < <("$PYTHON" - <<'PY'
import os, hashlib, base64, sys
verifier = base64.urlsafe_b64encode(os.urandom(32)).rstrip(b"=").decode()
challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
sys.stdout.write(f"{verifier} {challenge}\n")
PY
)
VERIFIER2="${VERIFIER2//$'\r'/}"
CHALLENGE2="${CHALLENGE2//$'\r'/}"
AUTH_URL2="$BASE/authorize?response_type=code&client_id=$(urlencode "$CLIENT_ID")&redirect_uri=$(urlencode "$REDIRECT_URI")&state=pkce&code_challenge=$(urlencode "$CHALLENGE2")&code_challenge_method=S256&scope=$(urlencode mcp:tools)&resource=$(urlencode "$MCP")"
LOGIN_URL2="$(curl -sk -D - -o /dev/null "$AUTH_URL2" | awk 'tolower($1)=="location:"{print $2}' | tr -d '\r' | tail -n1)"
[[ "$LOGIN_URL2" == http* ]] || LOGIN_URL2="$BASE${LOGIN_URL2}"
TICKET2="${LOGIN_URL2##*ticket=}"
CALLBACK2="$(curl -sk -D - -o /dev/null -X POST "$BASE/login" --data-urlencode "ticket=$TICKET2" | awk 'tolower($1)=="location:"{print $2}' | tr -d '\r' | tail -n1)"
CODE2="$("$PYTHON" - "$CALLBACK2" <<'PY'
import sys, urllib.parse
qs = urllib.parse.parse_qs(urllib.parse.urlparse(sys.argv[1]).query)
print((qs.get("code") or [""])[0])
PY
)"
BAD_PKCE="$(curl -sk -X POST "$BASE/token" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "code=$CODE2" \
  --data-urlencode "redirect_uri=$REDIRECT_URI" \
  --data-urlencode "client_id=$CLIENT_ID" \
  --data-urlencode "code_verifier=wrong-verifier-value-that-will-not-match" \
  --data-urlencode "resource=$MCP")"
echo "$BAD_PKCE" | grep -q 'invalid_grant' && pass "Wrong PKCE verifier is rejected" || fail "PKCE mismatch: $BAD_PKCE"

EXPIRED="$(curl -sk "$BASE/dev/expired-token")"
EXPIRED_TOKEN="$(json_get access_token "$EXPIRED")"
EXPIRED_HEADERS="$(curl -sk -D - -o /tmp/mcp_expired.json -X POST "$MCP" \
  -H "Authorization: Bearer $EXPIRED_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "MCP-Protocol-Version: $PROTOCOL" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"server/discover\",\"params\":{\"_meta\":$(mcp_meta)}}")"
echo "$EXPIRED_HEADERS" | grep -q 'HTTP/.* 401' && pass "Expired JWT is rejected" || fail "Expired JWT: $EXPIRED_HEADERS"

INVALID_HEADERS="$(curl -sk -D - -o /tmp/mcp_invalid.json -X POST "$MCP" \
  -H "Authorization: Bearer not-a-jwt" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "MCP-Protocol-Version: $PROTOCOL" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"server/discover\",\"params\":{\"_meta\":$(mcp_meta)}}")"
echo "$INVALID_HEADERS" | grep -q 'HTTP/.* 401' && pass "Invalid JWT is rejected" || fail "Invalid JWT: $INVALID_HEADERS"

if [[ "$FAILED" == "1" ]]; then
  echo
  echo "Smoke tests failed."
  exit 1
fi

echo
echo "All smoke tests passed."
