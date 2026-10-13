-- MVP 1.1: spirit pets (Combat), meridian allocations (Cultivation), daily quests, achievements and check-ins (Quests).

CREATE TABLE combat.spirit_pets (
    character_id     uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    npc_id           text        NOT NULL CHECK (npc_id ~ '^[a-z_]{1,64}$'),
    xp               integer     NOT NULL CHECK (xp >= 0),
    tamed_in_battle  uuid        NOT NULL,
    tamed_at         timestamptz NOT NULL
);

CREATE TABLE cultivation.meridians (
    character_id  uuid    NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    stat          text    NOT NULL CHECK (stat IN ('strength', 'spirit', 'agility', 'constitution', 'insight', 'willpower', 'focus', 'elementalAffinity', 'fate')),
    points        integer NOT NULL CHECK (points > 0),
    PRIMARY KEY (character_id, stat)
);

CREATE SCHEMA IF NOT EXISTS quests;

-- Progress of one character on one quest of one world day (or 'life' for achievements).
CREATE TABLE quests.progress (
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    period        text        NOT NULL CHECK (period ~ '^(\d{4}-\d{2}-\d{2}|life)$'),
    quest_id      text        NOT NULL CHECK (quest_id ~ '^[a-z0-9_]{1,64}$'),
    progress      bigint      NOT NULL CHECK (progress >= 0),
    claimed_at    timestamptz NULL,
    PRIMARY KEY (character_id, period, quest_id)
);

-- Already-counted events (outbox redelivery must not count twice).
CREATE TABLE quests.counted_events (
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    message_id    uuid        NOT NULL,
    counted_at    timestamptz NOT NULL,
    PRIMARY KEY (character_id, message_id)
);

CREATE TABLE quests.checkins (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    last_day      date        NOT NULL,
    streak        integer     NOT NULL CHECK (streak >= 1),
    best_streak   integer     NOT NULL CHECK (best_streak >= streak),
    updated_at    timestamptz NOT NULL
);
