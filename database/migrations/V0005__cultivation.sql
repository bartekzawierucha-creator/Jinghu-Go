-- Cultivation: per-character progress and an append-only log of breakthrough attempts (replayable rolls).

CREATE SCHEMA cultivation;

CREATE TABLE cultivation.states (
    character_id          uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id            uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    path                  text        NOT NULL CHECK (path IN ('qi', 'body', 'flame', 'poison')),
    realm_index           integer     NOT NULL CHECK (realm_index >= 0),
    stage                 integer     NOT NULL CHECK (stage >= 1),
    qi                    bigint      NOT NULL CHECK (qi >= 0),
    version               integer     NOT NULL CHECK (version >= 1),
    deviation_until       timestamptz NULL,
    meditation_started_at timestamptz NULL,
    daily_date            date        NOT NULL,
    daily_meditation_qi   bigint      NOT NULL CHECK (daily_meditation_qi >= 0),
    daily_steps           integer     NOT NULL CHECK (daily_steps >= 0),
    last_steps_report_at  timestamptz NULL,
    updated_at            timestamptz NOT NULL
);
CREATE INDEX cultivation_states_account_idx ON cultivation.states (account_id);

-- A cultivation state must belong to the same account and path as its character.
CREATE FUNCTION cultivation.states_match_character() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM characters.characters c
                   WHERE c.id = NEW.character_id AND c.account_id = NEW.account_id AND c.path = NEW.path) THEN
        RAISE EXCEPTION 'cultivation state does not match its character (account/path)' USING ERRCODE = 'foreign_key_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER cultivation_states_match_character
    BEFORE INSERT OR UPDATE OF character_id, account_id, path ON cultivation.states
    FOR EACH ROW EXECUTE FUNCTION cultivation.states_match_character();

CREATE TABLE cultivation.breakthroughs (
    id             uuid          PRIMARY KEY,
    character_id   uuid          NOT NULL REFERENCES characters.characters (id) ON DELETE RESTRICT,
    account_id     uuid          NOT NULL REFERENCES identity.accounts (id) ON DELETE RESTRICT,
    from_realm     integer       NOT NULL CHECK (from_realm >= 0),
    from_stage     integer       NOT NULL CHECK (from_stage >= 1),
    to_realm       integer       NOT NULL CHECK (to_realm >= 0),
    to_stage       integer       NOT NULL CHECK (to_stage >= 1),
    success        boolean       NOT NULL,
    chance_ppm     integer       NOT NULL CHECK (chance_ppm BETWEEN 1 AND 999999),
    roll           integer       NOT NULL CHECK (roll BETWEEN 0 AND 999999),
    seed           numeric(20,0) NOT NULL CHECK (seed BETWEEN 0 AND 18446744073709551615),
    qi_spent       bigint        NOT NULL CHECK (qi_spent >= 0),
    rules_version  text          NOT NULL,
    occurred_at    timestamptz   NOT NULL,
    CHECK (success = (roll < chance_ppm)),
    CHECK (success OR (to_realm = from_realm AND to_stage = from_stage))
);
CREATE INDEX cultivation_breakthroughs_character_idx ON cultivation.breakthroughs (character_id, id);

CREATE TRIGGER cultivation_breakthroughs_append_only
    BEFORE UPDATE OR DELETE ON cultivation.breakthroughs
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();
