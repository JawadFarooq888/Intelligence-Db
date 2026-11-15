#!/usr/bin/env bash
# Starts the API (http://localhost:5080) and the web app (http://localhost:5173) together.
# Ctrl+C stops both.
set -euo pipefail
cd "$(dirname "$0")/.."

trap 'kill 0' EXIT

dotnet run --project src/IntelligenceDB.Api --launch-profile http &
(cd src/IntelligenceDB.Web && npm run dev) &

wait
