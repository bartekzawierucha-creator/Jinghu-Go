-- Qi granted to cultivation by other modules' events (e.g. PvE victories). One grant per (event, character).

CREATE TABLE cultivation.reward_grants (
    source_id     uuid        NOT NULL,
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    qi            bigint      NOT NULL CHECK (qi > 0),
    granted_at    timestamptz NOT NULL,
    PRIMARY KEY (source_id, character_id)
);
