-- 2.3: Jianghu news — public lines about notable deeds (names only, no locations); kept 30 days.
CREATE TABLE social.news (
    id            uuid        PRIMARY KEY,
    source_id     uuid        NOT NULL UNIQUE,
    kind          text        NOT NULL CHECK (kind ~ '^[a-z_]{1,32}$'),
    character_id  uuid        NULL REFERENCES characters.characters (id) ON DELETE SET NULL,
    text          text        NOT NULL CHECK (char_length(text) BETWEEN 1 AND 300),
    created_at    timestamptz NOT NULL
);
CREATE INDEX news_created_idx ON social.news (created_at);
