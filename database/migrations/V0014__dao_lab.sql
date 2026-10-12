-- Dao Lab: player-designed techniques (immutable), who knows them, and technique scrolls as unique items.

CREATE TABLE combat.custom_techniques (
    id                   text        PRIMARY KEY CHECK (id ~ '^dao_[a-p]{16}$'),
    author_character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE RESTRICT,
    author_account_id    uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE RESTRICT,
    author_name          text        NOT NULL CHECK (length(author_name) BETWEEN 1 AND 64),
    slot                 text        NOT NULL CHECK (slot IN ('attack', 'defense', 'movement', 'counter')),
    definition           jsonb       NOT NULL CHECK (jsonb_typeof(definition) = 'object' AND definition->>'id' = id AND definition->>'slot' = slot),
    points               integer     NOT NULL CHECK (points >= 0),
    allowance            integer     NOT NULL CHECK (allowance >= points),       -- only designs within budget are stored
    rules_version        text        NOT NULL,
    created_at           timestamptz NOT NULL
);
CREATE INDEX custom_techniques_author_idx ON combat.custom_techniques (author_character_id);

-- Designs are referenced by frozen battle snapshots and by scrolls in circulation: they never change or disappear.
CREATE TRIGGER custom_techniques_immutable BEFORE UPDATE OR DELETE ON combat.custom_techniques
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

CREATE TABLE combat.known_techniques (
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    technique_id  text        NOT NULL REFERENCES combat.custom_techniques (id) ON DELETE RESTRICT,
    source        text        NOT NULL CHECK (source IN ('designed', 'scroll')),
    learned_at    timestamptz NOT NULL,
    PRIMARY KEY (character_id, technique_id)
);

-- Scrolls: a unique item may carry an immutable reference (the technique it teaches).
ALTER TABLE inventory.items ADD COLUMN bound_ref text NULL CHECK (bound_ref IS NULL OR bound_ref ~ '^[a-z_]{1,64}$');

CREATE OR REPLACE FUNCTION inventory.items_match_character() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM characters.characters c WHERE c.id = NEW.character_id AND c.account_id = NEW.account_id) THEN
        RAISE EXCEPTION 'item owner does not match character account' USING ERRCODE = 'foreign_key_violation';
    END IF;
    IF TG_OP = 'UPDATE' AND (NEW.item_id <> OLD.item_id OR NEW.version <> OLD.version + 1 OR OLD.state = 'destroyed'
                             OR NEW.bound_ref IS DISTINCT FROM OLD.bound_ref) THEN
        RAISE EXCEPTION 'illegal item update' USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

ALTER TABLE market.listings ADD COLUMN instance_ref text NULL CHECK (instance_ref IS NULL OR instance_ref ~ '^[a-z_]{1,64}$');
