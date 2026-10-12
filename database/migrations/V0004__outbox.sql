-- Transactional outbox: integration events written in the same transaction as the change that caused them.

CREATE TABLE platform.outbox_messages (
    id              uuid        PRIMARY KEY,
    type            text        NOT NULL CHECK (type ~ '^[a-z_]+\.[a-z_.]+$'),
    payload         jsonb       NOT NULL,
    correlation_id  text        NULL CHECK (correlation_id IS NULL OR length(correlation_id) <= 64),
    occurred_at     timestamptz NOT NULL,
    available_at    timestamptz NOT NULL,
    attempts        integer     NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    status          text        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'processed', 'dead')),
    processed_at    timestamptz NULL,
    last_error      text        NULL,
    CHECK ((status = 'processed') = (processed_at IS NOT NULL))
);
-- Dispatcher scan: only pending rows, oldest first.
CREATE INDEX outbox_pending_idx ON platform.outbox_messages (id) WHERE status = 'pending';
-- Retention job: processed rows older than 7 days are deleted (docs/operations/data-retention.md).
CREATE INDEX outbox_processed_at_idx ON platform.outbox_messages (processed_at) WHERE status = 'processed';
