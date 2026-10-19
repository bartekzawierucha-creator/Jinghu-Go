-- 1.9: friends, direct messages between friends, mail with attachments (stones / stackable tradable items).
-- No location data. Messages are kept 90 days (retention job); claimed mail too.

CREATE SCHEMA social;

-- One row per direction. Pending: only requester -> target. Accepted: both directions.
CREATE TABLE social.friends (
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    other_id      uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    status        text        NOT NULL CHECK (status IN ('pending', 'accepted')),
    created_at    timestamptz NOT NULL,
    PRIMARY KEY (character_id, other_id),
    CHECK (character_id <> other_id)
);
CREATE INDEX friends_other_idx ON social.friends (other_id);

CREATE TABLE social.messages (
    id            uuid        PRIMARY KEY,
    from_id       uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    to_id         uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    text          text        NOT NULL CHECK (char_length(text) BETWEEN 1 AND 300 AND text !~ '[[:cntrl:]]'),
    created_at    timestamptz NOT NULL,
    read_at       timestamptz NULL
);
CREATE INDEX messages_pair_idx ON social.messages (from_id, to_id, id DESC);
CREATE INDEX messages_to_unread_idx ON social.messages (to_id) WHERE read_at IS NULL;
CREATE INDEX messages_retention_idx ON social.messages (created_at);

CREATE TABLE social.mail (
    id            uuid        PRIMARY KEY,
    from_id       uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    from_name     text        NOT NULL CHECK (char_length(from_name) BETWEEN 1 AND 64),
    to_id         uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    text          text        NOT NULL CHECK (char_length(text) <= 300 AND text !~ '[[:cntrl:]]'),
    stones        bigint      NOT NULL CHECK (stones >= 0),
    item_id       text        NULL CHECK (item_id ~ '^[a-z0-9_]{1,80}$'),
    quantity      bigint      NOT NULL CHECK (quantity >= 0),
    created_at    timestamptz NOT NULL,
    claimed_at    timestamptz NULL,
    CHECK ((item_id IS NULL) = (quantity = 0))
);
CREATE INDEX mail_to_idx ON social.mail (to_id, id DESC);
CREATE INDEX mail_from_idx ON social.mail (from_id, created_at);
