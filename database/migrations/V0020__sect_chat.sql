-- MVP 1.4: sect chat. Plain text lines between members of one sect; no location, kept 30 days (retention job).

CREATE TABLE sects.chat_messages (
    id            uuid        PRIMARY KEY,
    sect_id       uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE CASCADE,
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    name          text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 64),
    text          text        NOT NULL CHECK (char_length(text) BETWEEN 1 AND 300 AND text !~ '[[:cntrl:]]'),
    created_at    timestamptz NOT NULL
);
CREATE INDEX chat_messages_sect_idx ON sects.chat_messages (sect_id, id DESC);
CREATE INDEX chat_messages_rate_idx ON sects.chat_messages (character_id, created_at);
CREATE INDEX chat_messages_retention_idx ON sects.chat_messages (created_at);
