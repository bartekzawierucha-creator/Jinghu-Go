-- MVP 1.5: cultivation-3 (66 realms × 12 stages, continuous Qi flow), tribulation and arena battles.

-- Everything about a cultivator beyond realm/stage/Qi (flow mode, vessel, crystals, method, heart demons, qualities,
-- karma, counters, offline summary) as one versioned JSON document.
CREATE TABLE cultivation.profiles (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    data          jsonb       NOT NULL,
    version       integer     NOT NULL CHECK (version >= 1),
    updated_at    timestamptz NOT NULL
);

-- 12 stages per realm (was up to 9); realm index up to 65
ALTER TABLE cultivation.states ADD CONSTRAINT states_stage_range CHECK (stage BETWEEN 1 AND 12);
ALTER TABLE cultivation.states ADD CONSTRAINT states_realm_range CHECK (realm_index BETWEEN 0 AND 65);

-- battles: heavenly tribulations, spirits to subdue (heavenly flames) and arena duels against a player's frozen copy are fought by the server AI
ALTER TABLE combat.battles DROP CONSTRAINT battles_kind_check;
ALTER TABLE combat.battles ADD CONSTRAINT battles_kind_check CHECK (kind IN ('pve', 'pvp', 'tower', 'boss', 'tribulation', 'arena', 'spirit'));
ALTER TABLE combat.battles DROP CONSTRAINT battles_context_check;
ALTER TABLE combat.battles ADD CONSTRAINT battles_context_check CHECK (context IS NULL OR context ~ '^(tower|boss|trib|arena|spirit):[0-9a-f-]{1,40}$');
ALTER TABLE combat.battles DROP CONSTRAINT battles_check;
ALTER TABLE combat.battles ADD CONSTRAINT battles_check CHECK (
       (kind = 'pve' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NULL)
    OR (kind = 'pvp' AND npc_id IS NULL AND character_b IS NOT NULL AND account_b IS NOT NULL AND character_b <> character_a AND context IS NULL)
    OR (kind = 'tower' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'tower:%')
    OR (kind = 'boss' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'boss:%')
    OR (kind = 'tribulation' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'trib:%')
    OR (kind = 'arena' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'arena:%')
    OR (kind = 'spirit' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'spirit:%'));
ALTER TABLE combat.battles DROP CONSTRAINT battles_reward_check;
ALTER TABLE combat.battles ADD CONSTRAINT battles_reward_check CHECK (reward_qi = 0 OR (kind IN ('pve', 'tower', 'boss', 'arena') AND status = 'finished'));

-- ranked arena: rating, record and the frozen copy (battle snapshot) others fight against
CREATE TABLE combat.arena_entries (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id    uuid        NOT NULL,
    name          text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 64),
    rating        integer     NOT NULL CHECK (rating BETWEEN 0 AND 10000),
    wins          integer     NOT NULL CHECK (wins >= 0),
    losses        integer     NOT NULL CHECK (losses >= 0),
    defense       jsonb       NOT NULL,
    updated_at    timestamptz NOT NULL,
    version       integer     NOT NULL CHECK (version >= 1)
);
CREATE INDEX arena_rating_idx ON combat.arena_entries (rating DESC, character_id);

CREATE TABLE combat.arena_claims (
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    week          text        NOT NULL CHECK (week ~ '^[0-9]{4}-W[0-9]{2}$'),
    claimed_at    timestamptz NOT NULL,
    PRIMARY KEY (character_id, week)
);

-- exactly-once guards of the profile event handlers (battle/craft/gather counters, pill instability, tribulations)
CREATE TABLE cultivation.handled_events (
    id            uuid        PRIMARY KEY,
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    handled_at    timestamptz NOT NULL
);
CREATE INDEX cultivation_handled_events_retention_idx ON cultivation.handled_events (handled_at);
