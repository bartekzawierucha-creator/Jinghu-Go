-- Platform: extensions, idempotency records, append-only audit log.
-- Rule: applied migrations are never edited; changes go into a new Vnnnn file (checksums are enforced).

CREATE EXTENSION IF NOT EXISTS postgis;   -- needed from Stage 5 (Geolocation/World); enabled early to fail fast on hosts without it

CREATE SCHEMA platform;

-- Generic guard for append-only tables.
CREATE FUNCTION platform.forbid_modification() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '% on %.% is forbidden: table is append-only', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TABLE platform.idempotency_records (
    account_id     uuid        NOT NULL,
    operation      text        NOT NULL CHECK (length(operation) BETWEEN 1 AND 100),
    key            text        NOT NULL CHECK (key ~ '^[A-Za-z0-9_-]{8,128}$'),
    request_hash   char(64)    NOT NULL CHECK (request_hash ~ '^[0-9A-F]{64}$'),
    response_json  jsonb       NOT NULL,
    created_at     timestamptz NOT NULL,
    PRIMARY KEY (account_id, operation, key)
);
-- Retention: rows older than 7 days may be purged by a scheduled job (docs/operations/data-retention.md).
CREATE INDEX idempotency_records_created_at_idx ON platform.idempotency_records (created_at);

CREATE TABLE platform.audit_log (
    id              uuid        PRIMARY KEY,
    account_id      uuid        NULL,
    action          text        NOT NULL CHECK (length(action) BETWEEN 1 AND 100),
    entity_type     text        NOT NULL,
    entity_id       text        NOT NULL,
    data            jsonb       NOT NULL DEFAULT '{}'::jsonb,
    correlation_id  text        NULL CHECK (correlation_id IS NULL OR length(correlation_id) <= 64),
    occurred_at     timestamptz NOT NULL
);
CREATE INDEX audit_log_entity_idx  ON platform.audit_log (entity_type, entity_id, occurred_at);
CREATE INDEX audit_log_account_idx ON platform.audit_log (account_id, occurred_at) WHERE account_id IS NOT NULL;

CREATE TRIGGER audit_log_append_only
    BEFORE UPDATE OR DELETE ON platform.audit_log
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();
