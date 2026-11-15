#!/usr/bin/env bash
# Runs once when the Codespace / dev container is created.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> Restoring .NET tools and packages"
dotnet tool restore
dotnet restore IntelligenceDB.slnx

# The first EF Core migration is generated here (it needs the .NET SDK, which the author's
# machine did not have). Commit the generated files so this step is skipped next time.
if ! ls src/IntelligenceDB.Infrastructure/Repository/Migrations/*_InitialCreate.cs >/dev/null 2>&1; then
  echo "==> Creating the initial repository migration"
  dotnet ef migrations add InitialCreate \
    --project src/IntelligenceDB.Infrastructure \
    --startup-project src/IntelligenceDB.Api \
    --output-dir Repository/Migrations
fi

echo "==> Installing web dependencies"
(cd src/IntelligenceDB.Web && npm ci)

echo ""
echo "Done. Start everything with:  bash scripts/dev.sh"
