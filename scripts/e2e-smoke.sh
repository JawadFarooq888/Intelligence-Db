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

echo "== Collector (Worker) stores metrics, databases, backups and a health score"
for i in $(seq 1 40); do
  ov=$(curl -fs "${AUTH[@]}" "$API/api/servers/$SERVER_ID/overview" || true)
  if echo "$ov" | jq -e '.latest != null and .databases.total > 0 and .health != null and (.settings | length) > 0' >/dev/null 2>&1; then break; fi
  sleep 5
  [ "$i" = 40 ] && fail "worker did not collect data: $ov"
done
ok "overview has metrics, $(echo "$ov" | jq '.databases.total') databases, $(echo "$ov" | jq '.settings | length') settings, health $(echo "$ov" | jq '.health.score')"
echo "$ov" | jq -e '.configAdvice | map(.code) | index("CFG_MAXMEM_DEFAULT")' >/dev/null || fail "config advice missing: $(echo "$ov" | jq -c '.configAdvice')"
ok "best-practice advice: max server memory not set"
echo "$ov" | jq -e '[.collectors[] | select(.source == "worker")] | length >= 5' >/dev/null || fail "collector status: $(echo "$ov" | jq -c '.collectors')"
ok "collector status per collector (source: worker)"
perf=$(curl -fs "${AUTH[@]}" "$API/api/servers/$SERVER_ID/performance?hours=1") || fail "performance endpoint"
echo "$perf" | jq -e '.series | length >= 1' >/dev/null || fail "no metric series: $perf"
ok "performance series available"
dbs=$(curl -fs "${AUTH[@]}" "$API/api/servers/$SERVER_ID/databases") || fail "server databases"
MASTER_ID=$(echo "$dbs" | jq -r '.[] | select(.name == "master") | .id')
[ -n "$MASTER_ID" ] && [ "$MASTER_ID" != null ] || fail "master not listed: $dbs"
detail=$(curl -fs "${AUTH[@]}" "$API/api/databases/$MASTER_ID") || fail "database detail"
echo "$detail" | jq -e '(.files | length) >= 2' >/dev/null || fail "files missing: $detail"
echo "$detail" | jq -e '.advice | map(.code) | index("DB_NO_FULL_BACKUP")' >/dev/null || fail "missing-backup advice: $(echo "$detail" | jq -c '.advice')"
ok "database detail: files and 'no full backup' advice for master"
curl -fs "${AUTH[@]}" "$API/api/search?q=maste" | jq -e 'map(.kind) | index("database")' >/dev/null || fail "database search"
ok "global search finds databases"
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

echo "== Agent: register, run, add a server through it"
created=$(curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/agents" -d '{"name":"e2e-agent"}') || fail "create agent"
AGENT_ID=$(echo "$created" | jq -r '.agent.id'); AGENT_KEY=$(echo "$created" | jq -r '.key')
[[ "$AGENT_KEY" == idbk_* ]] || fail "agent key: $created"
ok "agent created (key shown once)"
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Content-Type: application/json" -H "X-Agent-Id: $AGENT_ID" -H "X-Agent-Key: wrong" "$API/api/agent/v1/heartbeat" -d '{}')
[ "$code" = 401 ] || fail "wrong agent key returned $code"
ok "wrong agent key rejected (401)"
Agent__PortalUrl="$API" Agent__AllowInsecureHttp=true Agent__AgentId="$AGENT_ID" Agent__AgentKey="$AGENT_KEY" \
  nohup dotnet run --project src/IntelligenceDB.Agent -c Release --no-build > agent.log 2>&1 &
AGENT_PID=$!
for i in $(seq 1 30); do
  curl -fs "${AUTH[@]}" "$API/api/agents" | jq -e --arg id "$AGENT_ID" '.[] | select(.id == $id) | .isOnline' >/dev/null && break
  sleep 3; [ "$i" = 30 ] && fail "agent never came online"
done
ok "agent online"
abody=$(echo "$body" | jq --arg aid "$AGENT_ID" '.displayName = "E2E-VIA-AGENT" | .groupId = null | .agentId = $aid')
aserver=$(curl -fs "${H[@]}" "${AUTH[@]}" -X POST "$API/api/servers" -d "$abody") || fail "create server via agent"
[ "$(echo "$aserver" | jq -r '.server.lastStatus')" = Online ] || fail "agent probe: $(echo "$aserver" | jq -r '.server.lastError')"
ASERVER_ID=$(echo "$aserver" | jq -r '.server.id')
echo "$aserver" | jq -e '.server.agentName == "e2e-agent"' >/dev/null || fail "agent name missing"
ok "server added through the agent and Online (connection test ran inside the agent)"
for i in $(seq 1 40); do
  aov=$(curl -fs "${AUTH[@]}" "$API/api/servers/$ASERVER_ID/overview" || true)
  if echo "$aov" | jq -e '.databases.total > 0 and ([.collectors[] | select(.source == "agent:e2e-agent")] | length) >= 3' >/dev/null 2>&1; then break; fi
  sleep 5; [ "$i" = 40 ] && fail "agent did not deliver data: $aov"
done
ok "agent delivered collected data ($(echo "$aov" | jq '.databases.total') databases)"
code=$(curl -s -o /dev/null -w '%{http_code}' "${AUTH[@]}" -X DELETE "$API/api/agents/$AGENT_ID")
[ "$code" = 409 ] || fail "deleting an agent that still has servers returned $code"
ok "agent with servers cannot be deleted"
kill $AGENT_PID 2>/dev/null || true
echo "== Delete"
code=$(curl -s -o /dev/null -w '%{http_code}' "${AUTH[@]}" -X DELETE "$API/api/servers/$SERVER_ID")
[ "$code" = 204 ] || fail "delete returned $code"
ok "server deleted"

echo ""
echo "E2E smoke test passed: $pass checks"
