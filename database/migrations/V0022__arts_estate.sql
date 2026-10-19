-- MVP 1.5: path arts — heavenly flames, venoms / toxin body / coatings / Gu, body tempering (+N), sword intent (⚔N),
-- path branches. One versioned JSON document per character; outbox events are applied once per (event, purpose).

CREATE SCHEMA arts;

CREATE TABLE arts.profiles (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    data          jsonb       NOT NULL,
    version       integer     NOT NULL CHECK (version >= 1),
    updated_at    timestamptz NOT NULL
);

CREATE TABLE arts.handled_events (
    id            uuid        PRIMARY KEY,
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    handled_at    timestamptz NOT NULL
);
CREATE INDEX handled_events_retention_idx ON arts.handled_events (handled_at);

-- The estate (garden plots, disciples, expeditions) as one versioned document per character.
CREATE SCHEMA estate;

CREATE TABLE estate.profiles (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    data          jsonb       NOT NULL,
    version       integer     NOT NULL CHECK (version >= 1),
    updated_at    timestamptz NOT NULL
);

-- Cauldron / forge queue: a batch paid when queued, rolled (seed fixed then) when collected. One per profession.
CREATE TABLE crafting.queue (
    id               uuid        PRIMARY KEY,
    character_id     uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id       uuid        NOT NULL,
    recipe_id        text        NOT NULL CHECK (recipe_id ~ '^[a-z0-9_]{1,64}$'),
    kind             text        NOT NULL CHECK (kind IN ('alchemy', 'forge')),
    times            integer     NOT NULL CHECK (times BETWEEN 1 AND 1000),
    chance_permille  integer     NOT NULL CHECK (chance_permille BETWEEN 0 AND 1000),
    seed             numeric(20) NOT NULL,
    stones           bigint      NOT NULL CHECK (stones >= 0),
    started_at       timestamptz NOT NULL,
    ready_at         timestamptz NOT NULL CHECK (ready_at >= started_at),
    UNIQUE (character_id, kind)
);

-- Sect development: buildings, weekly missions and the weekly sect boss (one document per sect).
CREATE TABLE sects.development (
    sect_id     uuid        PRIMARY KEY REFERENCES sects.sects (id) ON DELETE CASCADE,
    data        jsonb       NOT NULL,
    version     integer     NOT NULL CHECK (version >= 1),
    updated_at  timestamptz NOT NULL
);

CREATE TABLE sects.handled_events (
    id          uuid        PRIMARY KEY,
    handled_at  timestamptz NOT NULL
);
CREATE INDEX sects_handled_events_retention_idx ON sects.handled_events (handled_at);
