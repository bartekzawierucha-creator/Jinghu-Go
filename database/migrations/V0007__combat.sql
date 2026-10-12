-- Combat: loadouts, technique mastery, battles, submitted intents and the append-only turn log
-- (turn log + revealed seed reproduce every battle exactly).

CREATE SCHEMA combat;

CREATE TABLE combat.loadouts (
    character_id  uuid    PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id    uuid    NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    slots         jsonb   NOT NULL CHECK (jsonb_typeof(slots) = 'array' AND jsonb_array_length(slots) = 8),
    version       integer NOT NULL CHECK (version >= 1)
);

CREATE TABLE combat.technique_mastery (
    character_id  uuid    NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    technique_id  text    NOT NULL CHECK (technique_id ~ '^[a-z_]{1,64}$'),
    level         integer NOT NULL CHECK (level BETWEEN 1 AND 10),
    xp            integer NOT NULL CHECK (xp >= 0),
    PRIMARY KEY (character_id, technique_id)
);

CREATE TABLE combat.battles (
    id             uuid          PRIMARY KEY,
    kind           text          NOT NULL CHECK (kind IN ('pve', 'pvp')),
    status         text          NOT NULL CHECK (status IN ('pending_acceptance', 'active', 'finished', 'declined', 'expired')),
    rules_version  text          NOT NULL,
    seed           numeric(20,0) NOT NULL CHECK (seed BETWEEN 0 AND 18446744073709551615),
    seed_hash      char(64)      NOT NULL,
    character_a    uuid          NOT NULL REFERENCES characters.characters (id) ON DELETE RESTRICT,
    account_a      uuid          NOT NULL REFERENCES identity.accounts (id) ON DELETE RESTRICT,
    character_b    uuid          NULL REFERENCES characters.characters (id) ON DELETE RESTRICT,
    account_b      uuid          NULL REFERENCES identity.accounts (id) ON DELETE RESTRICT,
    npc_id         text          NULL,
    combatant_a    jsonb         NULL,
    combatant_b    jsonb         NULL,
    state          jsonb         NULL,
    version        integer       NOT NULL CHECK (version >= 1),
    deadline       timestamptz   NOT NULL,
    timeouts_a     integer       NOT NULL CHECK (timeouts_a >= 0),
    timeouts_b     integer       NOT NULL CHECK (timeouts_b >= 0),
    reward_qi      bigint        NOT NULL CHECK (reward_qi >= 0),
    created_at     timestamptz   NOT NULL,
    updated_at     timestamptz   NOT NULL,
    finished_at    timestamptz   NULL,
    CHECK ((kind = 'pve' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL)
        OR (kind = 'pvp' AND npc_id IS NULL AND character_b IS NOT NULL AND account_b IS NOT NULL AND character_b <> character_a)),
    CHECK (status NOT IN ('active', 'finished') OR (state IS NOT NULL AND combatant_a IS NOT NULL AND combatant_b IS NOT NULL)),
    CHECK ((status IN ('finished', 'declined', 'expired')) = (finished_at IS NOT NULL)),
    CHECK (reward_qi = 0 OR (kind = 'pve' AND status = 'finished'))
);
-- A character fights at most one active battle on each side.
CREATE UNIQUE INDEX battles_active_a_uq ON combat.battles (character_a) WHERE status = 'active';
CREATE UNIQUE INDEX battles_active_b_uq ON combat.battles (character_b) WHERE status = 'active';
CREATE INDEX battles_open_b_idx ON combat.battles (character_b) WHERE status IN ('active', 'pending_acceptance');
CREATE INDEX battles_open_a_idx ON combat.battles (character_a) WHERE status IN ('active', 'pending_acceptance');
CREATE INDEX battles_rewards_idx ON combat.battles (character_a, finished_at) WHERE kind = 'pve' AND reward_qi > 0;

CREATE TABLE combat.intents (
    battle_id     uuid        NOT NULL REFERENCES combat.battles (id) ON DELETE CASCADE,
    turn          integer     NOT NULL CHECK (turn >= 1),
    side          char(1)     NOT NULL CHECK (side IN ('a', 'b')),
    intent        jsonb       NOT NULL,
    submitted_at  timestamptz NOT NULL,
    PRIMARY KEY (battle_id, turn, side)          -- one action per side per turn
);

CREATE TABLE combat.turns (
    battle_id    uuid        NOT NULL REFERENCES combat.battles (id) ON DELETE RESTRICT,
    turn         integer     NOT NULL CHECK (turn >= 1),
    intent_a     jsonb       NOT NULL,
    intent_b     jsonb       NOT NULL,
    forced_a     boolean     NOT NULL,
    forced_b     boolean     NOT NULL,
    events       jsonb       NOT NULL,
    state_after  jsonb       NOT NULL,
    resolved_at  timestamptz NOT NULL,
    PRIMARY KEY (battle_id, turn)                -- a turn resolves exactly once
);

CREATE TRIGGER turns_append_only
    BEFORE UPDATE OR DELETE ON combat.turns
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();
