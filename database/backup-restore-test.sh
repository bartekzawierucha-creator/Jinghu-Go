#!/usr/bin/env bash
# Backup/restore drill: migrate a scratch database, seed representative data (ledgers, items, sect, territory, audit),
# pg_dump it (custom format), restore into a second database and prove the copy is identical and healthy:
#   1. every table's rows hash to the same value in source and restore;
#   2. the migrator reports the restored schema up to date (checksums travel with the dump);
#   3. economy reconciliation views are empty and append-only triggers still fire after restore.
# Usage: ADMIN_URL=postgres://user:pass@host:5432/postgres ./database/backup-restore-test.sh
set -euo pipefail
export PGOPTIONS="${PGOPTIONS:--c client_min_messages=warning}"
: "${ADMIN_URL:?ADMIN_URL (a role allowed to CREATE DATABASE) is required}"
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="jianghu_drill_src_$$"
DST="jianghu_drill_dst_$$"
BASE="${ADMIN_URL%/*}"
DUMP="$(mktemp -d)/jianghu.dump"
cleanup() {
  psql "$ADMIN_URL" -X -q -c "DROP DATABASE IF EXISTS $SRC WITH (FORCE)" -c "DROP DATABASE IF EXISTS $DST WITH (FORCE)" >/dev/null
  rm -rf "$(dirname "$DUMP")"
}
trap cleanup EXIT

psql "$ADMIN_URL" -X -q -v ON_ERROR_STOP=1 -c "CREATE DATABASE $SRC" -c "CREATE DATABASE $DST"
DATABASE_URL="$BASE/$SRC" "$HERE/migrate.sh" >/dev/null
psql "$BASE/$SRC" -X -q -v ON_ERROR_STOP=1 -f "$HERE/restore-drill-seed.sql"

pg_dump --format=custom --no-owner --file "$DUMP" "$BASE/$SRC"
pg_restore --no-owner --exit-on-error --dbname "$BASE/$DST" "$DUMP"

# Row hash of every table in the application schemas (+ schema_migrations).
HASH_SQL=$(cat <<'SQL'
SELECT string_agg(format('%s.%s=%s', schemaname, tablename,
         (xpath('/row/h/text()', query_to_xml(format('SELECT md5(coalesce(string_agg(t::text, %L ORDER BY t::text), %L)) AS h FROM %I.%I t',
                 '|', '', schemaname, tablename), false, true, '')))[1]::text), E'\n' ORDER BY schemaname, tablename)
FROM pg_tables
WHERE schemaname NOT IN ('pg_catalog', 'information_schema', 'tiger', 'tiger_data', 'topology') AND tablename <> 'spatial_ref_sys';
SQL
)
src_hash="$(psql "$BASE/$SRC" -X -q -t -A -c "$HASH_SQL")"
dst_hash="$(psql "$BASE/$DST" -X -q -t -A -c "$HASH_SQL")"
if [[ "$src_hash" != "$dst_hash" ]]; then
  diff <(echo "$src_hash") <(echo "$dst_hash") >&2 || true
  echo "FAILED: restored data differs from the source" >&2; exit 1
fi
echo "Tables identical after restore: $(echo "$src_hash" | wc -l)"

DATABASE_URL="$BASE/$DST" "$HERE/migrate.sh" | grep -q "Migrations up to date." || { echo "FAILED: restored schema not up to date" >&2; exit 1; }
echo "Migrator: restored schema up to date"

psql "$BASE/$DST" -X -q -t -A -v ON_ERROR_STOP=1 <<'SQL'
DO $$
BEGIN
    IF (SELECT count(*) FROM inventory.wallet_mismatches) + (SELECT count(*) FROM inventory.stack_mismatches)
       + (SELECT count(*) FROM sects.treasury_mismatches) + (SELECT count(*) FROM characters.destiny_ledger_mismatches) <> 0 THEN
        RAISE EXCEPTION 'reconciliation not clean after restore';
    END IF;
    IF (SELECT balance FROM inventory.wallets WHERE character_id = '00000000-0000-7000-8000-0000000000c1') <> 70 THEN
        RAISE EXCEPTION 'wallet balance lost in restore';
    END IF;
    BEGIN
        UPDATE platform.audit_log SET action = 'tampered';
        RAISE EXCEPTION 'append-only trigger missing after restore';
    EXCEPTION WHEN restrict_violation THEN NULL;
    END;
    BEGIN
        UPDATE inventory.wallets SET balance = 1;
        RAISE EXCEPTION 'ledger guard missing after restore';
    EXCEPTION WHEN restrict_violation THEN NULL;
    END;
END $$;
SQL
echo "Reconciliation clean, guards active: backup/restore drill PASSED"
