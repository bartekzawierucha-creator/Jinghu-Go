-- Inventory: stackable items and Spirit Stones live in append-only ledgers; the quantity/balance tables are
-- projections maintained ONLY by ledger triggers (direct UPDATEs are refused). Unique items (equipment) are rows
-- with optimistic versions and an append-only provenance log. See docs/game-design/economy.md.

CREATE SCHEMA inventory;

-- ---------------------------------------------------------------- stackables

CREATE TABLE inventory.stacks (
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    item_id       text        NOT NULL CHECK (item_id ~ '^[a-z_]{1,64}$'),
    quantity      bigint      NOT NULL CHECK (quantity >= 0),
    updated_at    timestamptz NOT NULL,
    PRIMARY KEY (character_id, item_id)
);

CREATE TABLE inventory.stack_entries (
    id              uuid        PRIMARY KEY,
    character_id    uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    item_id         text        NOT NULL CHECK (item_id ~ '^[a-z_]{1,64}$'),
    delta           bigint      NOT NULL CHECK (delta <> 0),
    quantity_after  bigint      NOT NULL CHECK (quantity_after >= 0),
    reason          text        NOT NULL CHECK (reason ~ '^[a-z_]{1,64}$'),
    reference_id    text        NOT NULL CHECK (length(reference_id) BETWEEN 1 AND 200),
    created_at      timestamptz NOT NULL,
    UNIQUE (character_id, item_id, reason, reference_id)        -- the same operation can never be applied twice
);
CREATE INDEX stack_entries_character_idx ON inventory.stack_entries (character_id, id);
CREATE INDEX stack_entries_reference_idx ON inventory.stack_entries (character_id, reason, reference_id);

CREATE FUNCTION inventory.stack_ledger_apply() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    current_quantity bigint;
BEGIN
    INSERT INTO inventory.stacks (character_id, item_id, quantity, updated_at)
    VALUES (NEW.character_id, NEW.item_id, 0, NEW.created_at)
    ON CONFLICT (character_id, item_id) DO NOTHING;

    SELECT quantity INTO current_quantity FROM inventory.stacks
    WHERE character_id = NEW.character_id AND item_id = NEW.item_id
    FOR UPDATE;

    IF NEW.quantity_after <> current_quantity + NEW.delta THEN
        RAISE EXCEPTION 'stack ledger mismatch: % + % <> %', current_quantity, NEW.delta, NEW.quantity_after USING ERRCODE = 'check_violation';
    END IF;

    PERFORM set_config('jianghu.ledger_write', 'on', true);
    UPDATE inventory.stacks SET quantity = NEW.quantity_after, updated_at = NEW.created_at
     WHERE character_id = NEW.character_id AND item_id = NEW.item_id;
    PERFORM set_config('jianghu.ledger_write', 'off', true);
    RETURN NEW;
END $$;

CREATE TRIGGER stack_entries_apply BEFORE INSERT ON inventory.stack_entries
    FOR EACH ROW EXECUTE FUNCTION inventory.stack_ledger_apply();
CREATE TRIGGER stack_entries_append_only BEFORE UPDATE OR DELETE ON inventory.stack_entries
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

-- ---------------------------------------------------------------- Spirit Stones

CREATE TABLE inventory.wallets (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    balance       bigint      NOT NULL CHECK (balance >= 0),
    updated_at    timestamptz NOT NULL
);

CREATE TABLE inventory.wallet_entries (
    id             uuid        PRIMARY KEY,
    character_id   uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    delta          bigint      NOT NULL CHECK (delta <> 0),
    balance_after  bigint      NOT NULL CHECK (balance_after >= 0),
    reason         text        NOT NULL CHECK (reason ~ '^[a-z_]{1,64}$'),
    reference_id   text        NOT NULL CHECK (length(reference_id) BETWEEN 1 AND 200),
    created_at     timestamptz NOT NULL,
    UNIQUE (character_id, reason, reference_id)
);
CREATE INDEX wallet_entries_character_idx ON inventory.wallet_entries (character_id, id);

CREATE FUNCTION inventory.wallet_ledger_apply() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    current_balance bigint;
BEGIN
    INSERT INTO inventory.wallets (character_id, balance, updated_at)
    VALUES (NEW.character_id, 0, NEW.created_at)
    ON CONFLICT (character_id) DO NOTHING;

    SELECT balance INTO current_balance FROM inventory.wallets WHERE character_id = NEW.character_id FOR UPDATE;

    IF NEW.balance_after <> current_balance + NEW.delta THEN
        RAISE EXCEPTION 'wallet ledger mismatch: % + % <> %', current_balance, NEW.delta, NEW.balance_after USING ERRCODE = 'check_violation';
    END IF;

    PERFORM set_config('jianghu.ledger_write', 'on', true);
    UPDATE inventory.wallets SET balance = NEW.balance_after, updated_at = NEW.created_at WHERE character_id = NEW.character_id;
    PERFORM set_config('jianghu.ledger_write', 'off', true);
    RETURN NEW;
END $$;

CREATE TRIGGER wallet_entries_apply BEFORE INSERT ON inventory.wallet_entries
    FOR EACH ROW EXECUTE FUNCTION inventory.wallet_ledger_apply();
CREATE TRIGGER wallet_entries_append_only BEFORE UPDATE OR DELETE ON inventory.wallet_entries
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

-- Projections change only inside the ledger triggers above.
CREATE FUNCTION inventory.projection_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF coalesce(current_setting('jianghu.ledger_write', true), 'off') <> 'on' THEN
        RAISE EXCEPTION '% may only change through ledger entries', TG_TABLE_NAME USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER stacks_guard BEFORE UPDATE ON inventory.stacks FOR EACH ROW EXECUTE FUNCTION inventory.projection_guard();
CREATE TRIGGER wallets_guard BEFORE UPDATE ON inventory.wallets FOR EACH ROW EXECUTE FUNCTION inventory.projection_guard();

-- ---------------------------------------------------------------- unique items

CREATE TABLE inventory.items (
    id             uuid        PRIMARY KEY,
    character_id   uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id     uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    item_id        text        NOT NULL CHECK (item_id ~ '^[a-z_]{1,64}$'),
    upgrade_level  integer     NOT NULL CHECK (upgrade_level BETWEEN 0 AND 100),
    state          text        NOT NULL CHECK (state IN ('bag', 'equipped', 'listed', 'destroyed')),
    version        integer     NOT NULL CHECK (version >= 1),
    created_at     timestamptz NOT NULL
);
CREATE INDEX items_character_idx ON inventory.items (character_id) WHERE state <> 'destroyed';

-- Owner consistency: the account must own the character (a transfer must update both together).
CREATE FUNCTION inventory.items_match_character() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM characters.characters c WHERE c.id = NEW.character_id AND c.account_id = NEW.account_id) THEN
        RAISE EXCEPTION 'item owner does not match character account' USING ERRCODE = 'foreign_key_violation';
    END IF;
    IF TG_OP = 'UPDATE' AND (NEW.item_id <> OLD.item_id OR NEW.version <> OLD.version + 1 OR OLD.state = 'destroyed') THEN
        RAISE EXCEPTION 'illegal item update' USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER items_match_character BEFORE INSERT OR UPDATE ON inventory.items
    FOR EACH ROW EXECUTE FUNCTION inventory.items_match_character();

-- At most one equipped item per (character, slot) is enforced in the service (slot comes from game config).

CREATE TABLE inventory.item_events (
    id                uuid        PRIMARY KEY,
    item_instance_id  uuid        NOT NULL REFERENCES inventory.items (id) ON DELETE CASCADE,
    character_id      uuid        NOT NULL,                     -- owner at the time (no FK: history outlives transfers)
    kind              text        NOT NULL CHECK (kind ~ '^[a-z_]{1,64}$'),
    data              jsonb       NOT NULL,
    created_at        timestamptz NOT NULL
);
CREATE INDEX item_events_instance_idx ON inventory.item_events (item_instance_id, id);

CREATE TRIGGER item_events_append_only BEFORE UPDATE OR DELETE ON inventory.item_events
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

-- ---------------------------------------------------------------- reward grants (exactly-once event handlers)

CREATE TABLE inventory.grants (
    source_id     uuid        NOT NULL,                         -- outbox message id
    character_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    kind          text        NOT NULL CHECK (kind ~ '^[a-z_]{1,64}$'),
    granted_at    timestamptz NOT NULL,
    PRIMARY KEY (source_id, character_id)
);

-- ---------------------------------------------------------------- reconciliation (economy anomaly alert)

CREATE VIEW inventory.wallet_mismatches AS
SELECT w.character_id, w.balance, coalesce(sum(e.delta), 0) AS ledger_sum
FROM inventory.wallets w
LEFT JOIN inventory.wallet_entries e ON e.character_id = w.character_id
GROUP BY w.character_id, w.balance
HAVING w.balance <> coalesce(sum(e.delta), 0);

CREATE VIEW inventory.stack_mismatches AS
SELECT s.character_id, s.item_id, s.quantity, coalesce(sum(e.delta), 0) AS ledger_sum
FROM inventory.stacks s
LEFT JOIN inventory.stack_entries e ON e.character_id = s.character_id AND e.item_id = s.item_id
GROUP BY s.character_id, s.item_id, s.quantity
HAVING s.quantity <> coalesce(sum(e.delta), 0);
