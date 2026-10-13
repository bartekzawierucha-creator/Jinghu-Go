-- MVP 1.4: Tower of Trials and the world boss. Both are fought as ordinary rows in combat.battles (new kinds
-- 'tower' and 'boss', side B played by the server AI); "context" says which floor / boss cycle a battle belongs to.

ALTER TABLE combat.battles ADD COLUMN context text NULL CHECK (context IS NULL OR context ~ '^(tower|boss):[0-9]{1,9}$');

ALTER TABLE combat.battles DROP CONSTRAINT battles_kind_check;
ALTER TABLE combat.battles ADD CONSTRAINT battles_kind_check CHECK (kind IN ('pve', 'pvp', 'tower', 'boss'));

ALTER TABLE combat.battles DROP CONSTRAINT battles_check;
ALTER TABLE combat.battles ADD CONSTRAINT battles_check CHECK (
       (kind = 'pve' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NULL)
    OR (kind = 'pvp' AND npc_id IS NULL AND character_b IS NOT NULL AND account_b IS NOT NULL AND character_b <> character_a AND context IS NULL)
    OR (kind = 'tower' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'tower:%')
    OR (kind = 'boss' AND npc_id IS NOT NULL AND character_b IS NULL AND account_b IS NULL AND context IS NOT NULL AND context LIKE 'boss:%'));

ALTER TABLE combat.battles DROP CONSTRAINT battles_check3;
ALTER TABLE combat.battles ADD CONSTRAINT battles_reward_check CHECK (reward_qi = 0 OR (kind IN ('pve', 'tower', 'boss') AND status = 'finished'));

-- daily world boss attempts
CREATE INDEX battles_context_idx ON combat.battles (character_a, kind, context, created_at) WHERE context IS NOT NULL;

CREATE TABLE combat.tower_progress (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id    uuid        NOT NULL,
    name          text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 64),
    best_floor    integer     NOT NULL CHECK (best_floor BETWEEN 0 AND 500),
    updated_at    timestamptz NOT NULL,
    version       integer     NOT NULL CHECK (version >= 1)
);
CREATE INDEX tower_progress_rank_idx ON combat.tower_progress (best_floor DESC, updated_at, character_id);

CREATE TABLE combat.world_bosses (
    cycle         integer     PRIMARY KEY CHECK (cycle >= 0),
    npc_id        text        NOT NULL CHECK (npc_id ~ '^[a-z_]{1,64}$'),
    pool_hp       integer     NOT NULL CHECK (pool_hp >= 1),
    hp_left       integer     NOT NULL CHECK (hp_left BETWEEN 0 AND pool_hp),
    defeated_at   timestamptz NULL,
    version       integer     NOT NULL CHECK (version >= 1),
    CHECK ((hp_left = 0) = (defeated_at IS NOT NULL))
);

CREATE TABLE combat.world_boss_damage (
    cycle         integer     NOT NULL REFERENCES combat.world_bosses (cycle) ON DELETE CASCADE,
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id    uuid        NOT NULL,
    name          text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 64),
    damage        bigint      NOT NULL CHECK (damage >= 0),
    attempts      integer     NOT NULL CHECK (attempts >= 0),
    claimed_at    timestamptz NULL,
    version       integer     NOT NULL CHECK (version >= 1),
    PRIMARY KEY (cycle, character_id)
);
CREATE INDEX world_boss_damage_rank_idx ON combat.world_boss_damage (cycle, damage DESC, character_id);
