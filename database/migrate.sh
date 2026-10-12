#!/usr/bin/env bash
# Applies database/migrations/V*.sql in order, each in its own transaction, recording a SHA-256
# checksum. Refuses to run if an already-applied migration file was modified.
# Usage: DATABASE_URL=postgres://user:pass@host:5432/db ./database/migrate.sh
set -euo pipefail
: "${DATABASE_URL:?DATABASE_URL is required}"
DIR="$(cd "$(dirname "$0")/migrations" && pwd)"
PSQL=(psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -X -q -t -A)

"${PSQL[@]}" -c "CREATE TABLE IF NOT EXISTS public.schema_migrations (
  version text PRIMARY KEY, checksum char(64) NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())"

shopt -s nullglob
for file in "$DIR"/V*__*.sql; do
  name="$(basename "$file")"
  version="${name%%__*}"
  sum="$(sha256sum "$file" | cut -d' ' -f1)"
  applied="$("${PSQL[@]}" -c "SELECT checksum FROM public.schema_migrations WHERE version = '$version'")"
  if [[ -n "$applied" ]]; then
    if [[ "$applied" != "$sum" ]]; then
      echo "ERROR: $name was modified after being applied (checksum mismatch). Create a new migration instead." >&2
      exit 1
    fi
    continue
  fi
  echo "Applying $name"
  "${PSQL[@]}" --single-transaction -f "$file" \
    -c "INSERT INTO public.schema_migrations (version, checksum) VALUES ('$version', '$sum')"
done
echo "Migrations up to date."
