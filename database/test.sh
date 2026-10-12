#!/usr/bin/env bash
# Creates a scratch database, applies all migrations, re-runs the migrator (must be a no-op), checks
# that tampering with an applied migration is detected, then runs database/tests/*.sql.
# Usage: ADMIN_URL=postgres://user:pass@host:5432/postgres ./database/test.sh
set -euo pipefail
export PGOPTIONS="${PGOPTIONS:--c client_min_messages=warning}"
: "${ADMIN_URL:?ADMIN_URL (a role allowed to CREATE DATABASE and CREATE EXTENSION postgis) is required}"
HERE="$(cd "$(dirname "$0")" && pwd)"
DB="jianghu_test_$$"
BASE="${ADMIN_URL%/*}"
export DATABASE_URL="$BASE/$DB"

psql "$ADMIN_URL" -X -q -v ON_ERROR_STOP=1 -c "CREATE DATABASE $DB"
cleanup() { psql "$ADMIN_URL" -X -q -c "DROP DATABASE IF EXISTS $DB WITH (FORCE)" >/dev/null; rm -rf "${TMP:-}"; }
trap cleanup EXIT

"$HERE/migrate.sh"
"$HERE/migrate.sh" | grep -q "Migrations up to date." || { echo "re-run was not a no-op" >&2; exit 1; }

# Tamper detection: a copy of the migrations dir with one applied file edited must be refused.
TMP="$(mktemp -d)"
cp -r "$HERE/migrations" "$TMP/" && cp "$HERE/migrate.sh" "$TMP/"
echo "-- tampered" >> "$TMP/migrations/V0001__platform.sql"
if "$TMP/migrate.sh" >/dev/null 2>&1; then echo "tampered migration was not detected" >&2; exit 1; fi
echo "Tamper detection: OK"

for t in "$HERE"/tests/*.sql; do
  echo "Running $(basename "$t")"
  psql "$DATABASE_URL" -X -q -t -A -v ON_ERROR_STOP=1 -f "$t" | grep -v "^$"
done
