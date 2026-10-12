#!/usr/bin/env bash
# One command to build and run every automated check.
#   JIANGHU_TEST_POSTGRES="Host=…;Username=…;Password=…;Database=postgres"  → also runs the suite on PostgreSQL
#   ADMIN_URL=postgres://user:pass@host:5432/postgres                       → also runs the SQL integrity suite
# Extra args go to dotnet build (e.g. --configfile <nuget.config>).
set -euo pipefail
cd "$(dirname "$0")"
dotnet build server/JianghuGo.sln -c Release --nologo "$@"
dotnet run --project server/tests/UnitTests -c Release --no-build
JIANGHU_TEST_STORAGE=InMemory dotnet run --project server/tests/IntegrationTests -c Release --no-build
if [[ -n "${JIANGHU_TEST_POSTGRES:-}" ]]; then
  JIANGHU_TEST_STORAGE=Postgres dotnet run --project server/tests/IntegrationTests -c Release --no-build
else
  echo "PostgreSQL integration suite: NOT RUN (set JIANGHU_TEST_POSTGRES)"
fi
if [[ -n "${ADMIN_URL:-}" ]]; then ./database/test.sh; else echo "Database SQL suite: NOT RUN (set ADMIN_URL)"; fi
