-- 2.3: bounty board — escrowed stones on a cultivator's head, paid to whoever defeats them in a duel or on the arena.
CREATE TABLE combat.bounties (
    id           uuid        PRIMARY KEY,
    target_id    uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    target_name  text        NOT NULL CHECK (char_length(target_name) BETWEEN 1 AND 64),
    poster_id    uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    stones       bigint      NOT NULL CHECK (stones BETWEEN 100 AND 100000),
    created_at   timestamptz NOT NULL,
    claimed_by   uuid        NULL REFERENCES characters.characters (id) ON DELETE SET NULL,
    closed_at    timestamptz NULL,
    status       text        NOT NULL CHECK (status IN ('open', 'claimed', 'withdrawn')),
    CHECK ((status = 'open') = (closed_at IS NULL))
);
CREATE INDEX bounties_target_open_idx ON combat.bounties (target_id) WHERE status = 'open';
CREATE INDEX bounties_open_idx ON combat.bounties (stones DESC) WHERE status = 'open';
CREATE INDEX bounties_poster_idx ON combat.bounties (poster_id) WHERE status = 'open';
