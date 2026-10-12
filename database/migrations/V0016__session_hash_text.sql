-- Performance fix found by the Stage 8 load test: token hashes were char(64) while the application binds text
-- parameters, so PostgreSQL compared access_token_hash::text = $1 and could not use the unique indexes — every
-- authenticated request scanned identity.sessions (and, under SERIALIZABLE, took a relation-wide predicate lock).
-- text keeps the same values (always exactly 64 hex characters, enforced by the existing CHECKs); indexes are rebuilt.
ALTER TABLE identity.sessions
    ALTER COLUMN access_token_hash TYPE text,
    ALTER COLUMN refresh_token_hash TYPE text;
