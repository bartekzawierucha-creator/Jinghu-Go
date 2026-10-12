#!/usr/bin/env bash
# One command to build and run every automated check that does not need Docker.
# Optional: ADMIN_URL=postgres://... runs the database suite too.  Extra args go to dotnet (e.g. --source <feed>).
set -euo pipefail
cd "$(dirname "$0")"
dotnet build server/JianghuGo.sln -c Release --nologo "$@"
dotnet run --project server/tests/UnitTests -c Release --no-build
dotnet run --project server/tests/IntegrationTests -c Release --no-build
if [[ -n "${ADMIN_URL:-}" ]]; then ./database/test.sh; else echo "Database suite: NOT RUN (set ADMIN_URL)"; fi
