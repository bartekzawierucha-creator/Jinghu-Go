-- 2.2: one burning talisman per character (stats/perks for some hours).
CREATE TABLE inventory.talismans (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    item_id       text        NOT NULL CHECK (item_id ~ '^[a-z0-9_]{1,80}$'),
    until         timestamptz NOT NULL
);
