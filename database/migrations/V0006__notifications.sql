-- Notifications: written by outbox handlers; idempotent per (account, source outbox message).

CREATE SCHEMA notifications;

CREATE TABLE notifications.notifications (
    id                 uuid        PRIMARY KEY,
    account_id         uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    type               text        NOT NULL CHECK (type ~ '^[a-z_]{1,64}$'),
    payload            jsonb       NOT NULL,
    source_message_id  uuid        NOT NULL,
    created_at         timestamptz NOT NULL,
    read_at            timestamptz NULL,
    UNIQUE (account_id, source_message_id)
);
-- Listing: newest first per account (id is time-ordered UUIDv7), optional unread filter.
CREATE INDEX notifications_account_idx ON notifications.notifications (account_id, id DESC);
CREATE INDEX notifications_unread_idx  ON notifications.notifications (account_id, id DESC) WHERE read_at IS NULL;
