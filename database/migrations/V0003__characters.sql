-- Characters: drafts (server-rolled), characters, Destiny Token ledger.

CREATE SCHEMA characters;

CREATE FUNCTION characters.stats_valid(s jsonb) RETURNS boolean
LANGUAGE sql IMMUTABLE AS $$
    SELECT CASE WHEN jsonb_typeof(s) <> 'array' OR jsonb_array_length(s) <> 9 THEN false ELSE
        (SELECT array_agg(e ->> 'id' ORDER BY ord) FROM jsonb_array_elements(s) WITH ORDINALITY AS t(e, ord))
            = ARRAY['strength','spirit','agility','constitution','insight','willpower','focus','elementalAffinity','fate']
        AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(s) AS e
                        WHERE jsonb_typeof(e -> 'value') <> 'number' OR (e ->> 'value')::numeric <> trunc((e ->> 'value')::numeric))
    END
$$;

CREATE TABLE characters.drafts (
    id                  uuid          PRIMARY KEY,
    account_id          uuid          NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    version             integer       NOT NULL CHECK (version >= 1),
    status              text          NOT NULL CHECK (status IN ('active', 'finalized')),
    rules_version       text          NOT NULL,
    last_seed           numeric(20,0) NOT NULL CHECK (last_seed BETWEEN 0 AND 18446744073709551615),
    background_trait    text          NOT NULL,
    bloodline_trait     text          NOT NULL,
    constitution_trait  text          NOT NULL,
    stats               jsonb         NOT NULL CHECK (characters.stats_valid(stats)),
    reroll_count        integer       NOT NULL CHECK (reroll_count >= 0),
    created_at          timestamptz   NOT NULL,
    updated_at          timestamptz   NOT NULL CHECK (updated_at >= created_at)
);
-- At most one active draft per account.
CREATE UNIQUE INDEX drafts_one_active_per_account_uq ON characters.drafts (account_id) WHERE status = 'active';

CREATE TABLE characters.characters (
    id                  uuid        PRIMARY KEY,
    account_id          uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE RESTRICT,
    name                text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 64 AND name = btrim(name)),
    normalized_name     text        NOT NULL CHECK (normalized_name = upper(normalized_name)),
    path                text        NOT NULL CHECK (path IN ('qi', 'body', 'flame', 'poison')),
    rules_version       text        NOT NULL,
    background_trait    text        NOT NULL,
    bloodline_trait     text        NOT NULL,
    constitution_trait  text        NOT NULL,
    stats               jsonb       NOT NULL CHECK (characters.stats_valid(stats)),
    source_draft_id     uuid        NOT NULL REFERENCES characters.drafts (id) ON DELETE RESTRICT,
    created_at          timestamptz NOT NULL
);
CREATE UNIQUE INDEX characters_normalized_name_uq ON characters.characters (normalized_name);
CREATE UNIQUE INDEX characters_source_draft_uq    ON characters.characters (source_draft_id);
CREATE INDEX characters_account_idx               ON characters.characters (account_id, created_at);

-- ---------------------------------------------------------------------------------------------
-- Destiny Token ledger. Entries are immutable; the balance row is maintained ONLY by the insert
-- trigger, which also verifies balance_after. A direct balance UPDATE is rejected.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE characters.destiny_token_balances (
    account_id  uuid        PRIMARY KEY REFERENCES identity.accounts (id) ON DELETE CASCADE,
    balance     integer     NOT NULL CHECK (balance >= 0),
    updated_at  timestamptz NOT NULL
);

CREATE TABLE characters.destiny_token_entries (
    id            uuid        PRIMARY KEY,
    account_id    uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    delta         integer     NOT NULL CHECK (delta <> 0),
    balance_after integer     NOT NULL CHECK (balance_after >= 0),
    reason        text        NOT NULL CHECK (reason IN ('initial_grant', 'reroll')),
    reference_id  text        NOT NULL CHECK (length(reference_id) BETWEEN 1 AND 200),
    created_at    timestamptz NOT NULL,
    UNIQUE (account_id, reason, reference_id)
);
CREATE INDEX destiny_token_entries_account_idx ON characters.destiny_token_entries (account_id, id);

CREATE FUNCTION characters.destiny_ledger_apply() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    current_balance integer;
BEGIN
    INSERT INTO characters.destiny_token_balances (account_id, balance, updated_at)
    VALUES (NEW.account_id, 0, NEW.created_at)
    ON CONFLICT (account_id) DO NOTHING;

    SELECT balance INTO current_balance
    FROM characters.destiny_token_balances
    WHERE account_id = NEW.account_id
    FOR UPDATE;                                   -- serialises concurrent postings per account

    IF NEW.balance_after <> current_balance + NEW.delta THEN
        RAISE EXCEPTION 'ledger mismatch: balance % + delta % <> balance_after %', current_balance, NEW.delta, NEW.balance_after
            USING ERRCODE = 'check_violation';
    END IF;

    PERFORM set_config('jianghu.ledger_write', 'on', true);
    UPDATE characters.destiny_token_balances
       SET balance = NEW.balance_after, updated_at = NEW.created_at
     WHERE account_id = NEW.account_id;
    PERFORM set_config('jianghu.ledger_write', 'off', true);
    RETURN NEW;
END $$;

CREATE TRIGGER destiny_token_entries_apply
    BEFORE INSERT ON characters.destiny_token_entries
    FOR EACH ROW EXECUTE FUNCTION characters.destiny_ledger_apply();

CREATE TRIGGER destiny_token_entries_append_only
    BEFORE UPDATE OR DELETE ON characters.destiny_token_entries
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

CREATE FUNCTION characters.destiny_balance_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF coalesce(current_setting('jianghu.ledger_write', true), 'off') <> 'on' THEN
        RAISE EXCEPTION 'destiny_token_balances may only change through ledger entries' USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER destiny_token_balances_guard
    BEFORE UPDATE ON characters.destiny_token_balances
    FOR EACH ROW EXECUTE FUNCTION characters.destiny_balance_guard();

-- Reconciliation: returns accounts whose stored balance differs from the sum of their ledger.
-- Expected to return zero rows; monitored as an economy anomaly alert (docs/operations/observability.md).
CREATE VIEW characters.destiny_ledger_mismatches AS
SELECT b.account_id, b.balance, coalesce(sum(e.delta), 0) AS ledger_sum
FROM characters.destiny_token_balances b
LEFT JOIN characters.destiny_token_entries e ON e.account_id = b.account_id
GROUP BY b.account_id, b.balance
HAVING b.balance <> coalesce(sum(e.delta), 0);
