-- World: spawns are procedural (not stored); only claims and Flame attunements are.
-- node_key encodes an ~500 m map cell: claims are deleted after 7 days (docs/operations/data-retention.md).

CREATE SCHEMA world;

CREATE TABLE world.node_claims (
    node_key      text        NOT NULL CHECK (node_key ~ '^-?[0-9]+\.-?[0-9]+\.-?[0-9]+\.[0-9]+$'),
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id    uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    kind          text        NOT NULL CHECK (kind ~ '^[a-z_]{1,64}$'),
    claimed_at    timestamptz NOT NULL,
    PRIMARY KEY (node_key, character_id)          -- each node once per character
);
CREATE INDEX node_claims_character_time_idx ON world.node_claims (character_id, claimed_at);
CREATE INDEX node_claims_retention_idx ON world.node_claims (claimed_at);

CREATE TABLE world.flame_attunements (
    character_id     uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    site_id          text        NOT NULL CHECK (site_id ~ '^[a-z_]{1,64}$'),
    last_attuned_at  timestamptz NOT NULL,
    count            integer     NOT NULL CHECK (count >= 1),
    PRIMARY KEY (character_id, site_id)
);
