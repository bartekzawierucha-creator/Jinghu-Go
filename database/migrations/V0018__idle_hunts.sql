-- MVP 1.3: idle hunts (automatic battles while the player is away). One hunt per character; the battles themselves
-- are ordinary rows in combat.battles / combat.turns, so this table only keeps the hunt and its running totals.

CREATE TABLE combat.idle_hunts (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id    uuid        NOT NULL,
    npc_id        text        NOT NULL CHECK (npc_id ~ '^[a-z_]{1,64}$'),
    started_at    timestamptz NOT NULL,
    settled_at    timestamptz NOT NULL CHECK (settled_at >= started_at),
    battles       integer     NOT NULL CHECK (battles >= 0),
    wins          integer     NOT NULL CHECK (wins >= 0),
    losses        integer     NOT NULL CHECK (losses >= 0 AND wins + losses <= battles),
    qi            bigint      NOT NULL CHECK (qi >= 0),
    version       integer     NOT NULL CHECK (version >= 1)
);
