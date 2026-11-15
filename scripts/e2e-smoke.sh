#!/usr/bin/env bash
# End-to-end smoke test against a running API (http://localhost:5080) and a real SQL Server.
# Used by CI; can also be run in the Codespace after "bash scripts/dev.sh":
#   ADMIN_EMAIL=... ADMIN_PASSWORD=... TARGET_HOST=sql TARGET_PASSWORD=... bash scripts/e2e-smoke.sh
set -euo pipefail

API="${API:-http://localhost:5080}"
ADMIN_EMAIL="${ADMIN_EMAIL:?}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:?}"
TARGET_HOST="${TARGET_HOST:-localhost}"
TARGET_PASSWORD="${TARGET_PASSWORD:?}"

pass=0
fail() { echo "::error::E2E FAILED: $*"; exit 1; }
ok() { pass=$((pass + 1)); echo "  ok - $*"; }
H=(-H "Content-Type: application/json" -H "X-Requested-With: IntelligenceDB")

echo "== Waiting for the API"
for i in $(seq 1 60); do
  if curl -fs "$API/health" >/dev/null; then break; fi
  sleep 3
  [ "$i" = 60 ] && fail "API did not become healthy"
done
ok "API healthy"

echo "== Security basics"
code=$(curl -s -o /dev/null -w '%{http_code}' "$API/api/servers")
[ "$code" = 401 ] || fail "anonymous /api/servers returned $code (expected 401)"
ok "anonymous request rejected (401)"

code=$(curl -s -o /dev/null -w '%{http_code}' "${H[@]}" -X POST "$API/api/auth/login" -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"wrong-Password1!\"}")
[ "$code" = 401 ] || fail "wrong password returned $code"
ok "wrong password rejected"

echo "== Admin sign-in"
login=$(curl -fs "${H[@]}" -X POST "$API/api/auth/login" -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}") || fail "admin login failed"
TOKEN=$(echo "$login" | jq -r '.session.accessToken')
[ -n "$TOKEN" ] && [ "$TOKEN" != null ] || fail "no access token: $login"
echo "$login" | jq -e '.session.user.roles | index("Admin")' >/dev/null || fail "admin role missing"
AUTH=(-H "Authorization: Bearer $TOKEN")
ok "admin signed in with Admin role"

echo "== Server group"
group=$(curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/server-groups" -d '{"name":"E2E Group"}') || fail "create group"
GROUP_ID=$(echo "$group" | jq -r '.id')
ok "group created ($GROUP_ID)"

echo "== Validation"
code=$(curl -s -o /tmp/v.json -w '%{http_code}' "${H[@]}" "${AUTH[@]}" -X POST "$API/api/servers" \
  -d '{"displayName":"bad","host":"x;Database=evil","declaredPlatform":"OnPremises","monitoring":{"authType":"SqlLogin","userName":"sa","secret":"x"}}')
[ "$code" = 400 ] || fail "invalid host returned $code"
jq -e '.errors.host' /tmp/v.json >/dev/null || fail "no host validation error: $(cat /tmp/v.json)"
ok "invalid host rejected with field error"

echo "== Add server + capability detection"
body=$(jq -n --arg host "$TARGET_HOST" --arg pw "$TARGET_PASSWORD" --argjson gid "$GROUP_ID" '{
  displayName: "E2E-SQL", host: $host, port: 1433, declaredPlatform: "OnPremises", environment: "Test",
  groupId: $gid, tags: ["e2e"], encrypt: true, trustServerCertificate: true,
  monitoring: { authType: "SqlLogin", userName: "sa", secret: $pw },
  admin: { authType: "SqlLogin", userName: "sa", secret: $pw } }')
server=$(curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/servers" -d "$body") || fail "create server"
SERVER_ID=$(echo "$server" | jq -r '.server.id')
status=$(echo "$server" | jq -r '.server.lastStatus')
[ "$status" = Online ] || fail "server status $status: $(echo "$server" | jq -r '.server.lastError')"
ok "server added and Online"
echo "$server" | jq -e '.server.detectedPlatform == "OnPremises"' >/dev/null || fail "platform not detected"
ok "platform detected: OnPremises"
echo "$server" | jq -e '.info.majorVersion >= 16' >/dev/null || fail "version not detected"
ok "version detected: $(echo "$server" | jq -r '.server.versionLabel')"
for cap in ServerStateDmvs MsdbAccess BackupHistory KillSession AgentJobControl; do
  echo "$server" | jq -e --arg k "$cap" '.capabilities[] | select(.key == $k) | .isSupported' >/dev/null \
    || fail "capability $cap not supported: $(echo "$server" | jq -c --arg k "$cap" '.capabilities[] | select(.key == $k)')"
done
ok "capabilities detected (DMVs, msdb, backups, kill, job control)"
if echo "$server" | grep -q "$TARGET_PASSWORD"; then fail "secret returned to the client"; fi
echo "$server" | jq -e '.monitoring.hasSecret == true' >/dev/null || fail "hasSecret flag missing"
ok "password never returned (hasSecret only)"

echo "== Update keeps the stored secret"
upd=$(echo "$body" | jq --arg rv "$(echo "$server" | jq -r '.rowVersion')" '.displayName = "E2E-SQL-renamed" | .monitoring.secret = null | .admin.secret = null | .rowVersion = $rv')
server2=$(curl -fs "${H[@]}" "${AUTH[@]}" -X PUT "$API/api/servers/$SERVER_ID" -d "$upd") || fail "update server"
[ "$(echo "$server2" | jq -r '.server.lastStatus')" = Online ] || fail "update lost the stored password"
ok "renamed without re-entering the password, still Online"

echo "== Unreachable server does not break anything"
bad=$(jq -n '{displayName:"E2E-DOWN", host:"127.0.0.1", port:1, declaredPlatform:"OnPremises", connectTimeoutSeconds:5,
  trustServerCertificate:true, monitoring:{authType:"SqlLogin", userName:"x", secret:"y"}}')
down=$(curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/servers" -d "$bad") || fail "create unreachable server"
[ "$(echo "$down" | jq -r '.server.lastStatus')" = Unreachable ] || fail "expected Unreachable"
ok "unreachable server saved with status Unreachable"

echo "== Dashboard + refresh + search + scripts"
dash=$(curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/dashboard/refresh") || fail "dashboard refresh"
echo "$dash" | jq -e '.totalServers == 2 and .online == 1 and .unreachable == 1' >/dev/null || fail "dashboard counts: $dash"
ok "dashboard: 2 servers, 1 online, 1 unreachable"
curl -fs "${AUTH[@]}" "$API/api/search?q=E2E" | jq -e 'length >= 2' >/dev/null || fail "search"
ok "global search finds servers"
curl -fs "${AUTH[@]}" "$API/api/servers/login-scripts/monitoring/onprem" | grep -q "IntelligenceDB_Monitor" || fail "login script"
ok "login script served"
code=$(curl -s -o /dev/null -w '%{http_code}' "${AUTH[@]}" "$API/api/servers/login-scripts/monitoring/..%2F..%2Fetc")
[ "$code" = 404 ] || fail "script path traversal returned $code"
ok "login script names are whitelisted"

echo "== Viewer limited to a group cannot change anything"
curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/users" -d "{\"email\":\"viewer@e2e.local\",\"displayName\":\"Viewer\",\"password\":\"Viewer!Pass2026\",\"role\":\"Viewer\",\"allServerGroups\":false,\"groupIds\":[$GROUP_ID]}" >/dev/null || fail "create viewer"
vlogin=$(curl -fs "${H[@]}" -X POST "$API/api/auth/login" -d '{"email":"viewer@e2e.local","password":"Viewer!Pass2026"}') || fail "viewer login"
VAUTH=(-H "Authorization: Bearer $(echo "$vlogin" | jq -r '.session.accessToken')")
vlist=$(curl -fs "${VAUTH[@]}" "$API/api/servers")
echo "$vlist" | jq -e 'length == 1 and .[0].displayName == "E2E-SQL-renamed"' >/dev/null || fail "viewer scope: $vlist"
ok "viewer sees only servers in their group"
code=$(curl -s -o /dev/null -w '%{http_code}' "${H[@]}" "${VAUTH[@]}" -X DELETE "$API/api/servers/$SERVER_ID")
[ "$code" = 403 ] || fail "viewer delete returned $code"
code=$(curl -s -o /dev/null -w '%{http_code}' "${VAUTH[@]}" "$API/api/users")
[ "$code" = 403 ] || fail "viewer users returned $code"
ok "viewer cannot delete servers or manage users (403)"

echo "== Delete"
code=$(curl -s -o /dev/null -w '%{http_code}' "${AUTH[@]}" -X DELETE "$API/api/servers/$SERVER_ID")
[ "$code" = 204 ] || fail "delete returned $code"
ok "server deleted"

echo ""
echo "E2E smoke test passed: $pass checks"
