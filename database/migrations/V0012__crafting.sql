-- Crafting: append-only log of every craft/upgrade request. Materials, stones and items move through the inventory
-- ledgers in the SAME transaction (reference_id = log id), so a log row without its ledger lines cannot exist.

CREATE SCHEMA crafting;

CREATE TABLE crafting.log (
    id               uuid          PRIMARY KEY,
    character_id     uuid          NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id       uuid          NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    kind             text          NOT NULL CHECK (kind IN ('craft', 'upgrade')),
    subject          text          NOT NULL CHECK (length(subject) BETWEEN 1 AND 64),   -- recipe id or item instance id
    attempts         integer       NOT NULL CHECK (attempts BETWEEN 1 AND 10),
    successes        integer       NOT NULL CHECK (successes BETWEEN 0 AND attempts),
    chance_permille  integer       NOT NULL CHECK (chance_permille BETWEEN 1 AND 1000),
    seed             numeric(20,0) NOT NULL CHECK (seed BETWEEN 0 AND 18446744073709551615),
    stones_spent     bigint        NOT NULL CHECK (stones_spent >= 0),
    from_level       integer       NULL CHECK (from_level >= 0),
    to_level         integer       NULL CHECK (to_level >= 0),
    rules_version    text          NOT NULL,
    occurred_at      timestamptz   NOT NULL,
    CHECK ((kind = 'upgrade') = (from_level IS NOT NULL AND to_level IS NOT NULL)),
    CHECK (kind <> 'upgrade' OR to_level = from_level + successes)
);
CREATE INDEX crafting_log_character_idx ON crafting.log (character_id, id);

CREATE TRIGGER crafting_log_append_only BEFORE UPDATE OR DELETE ON crafting.log
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();
