#!/usr/bin/env bash
# Starts the API (http://localhost:5080), the Worker (collector) and the web app (http://localhost:5173).
# Ctrl+C stops all of them.
set -euo pipefail
cd "$(dirname "$0")/.."

trap 'kill 0' EXIT

dotnet build IntelligenceDB.slnx -v q
dotnet run --project src/IntelligenceDB.Api --launch-profile http --no-build &
sleep 15 # let the API apply database migrations first
dotnet run --project src/IntelligenceDB.Worker --no-build &
(cd src/IntelligenceDB.Web && npm run dev) &

wait
