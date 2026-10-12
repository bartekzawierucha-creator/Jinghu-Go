-- Sects (membership, roles, applications, treasury ledger, history) and Territory (control, GvG wars, ownership history).

CREATE SCHEMA sects;

CREATE TABLE sects.sects (
    id                   uuid             PRIMARY KEY,
    name                 text             NOT NULL CHECK (length(name) BETWEEN 3 AND 64),
    normalized_name      text             NOT NULL UNIQUE,                 -- names are never reused, even after disbanding
    leader_character_id  uuid             NOT NULL REFERENCES characters.characters (id) ON DELETE RESTRICT,
    leader_account_id    uuid             NOT NULL REFERENCES identity.accounts (id) ON DELETE RESTRICT,
    member_count         integer          NOT NULL CHECK (member_count >= 0),
    seat_lat             double precision NULL CHECK (seat_lat BETWEEN -90 AND 90),
    seat_lon             double precision NULL CHECK (seat_lon BETWEEN -180 AND 180),
    seat_set_at          timestamptz      NULL,
    version              integer          NOT NULL CHECK (version >= 1),
    created_at           timestamptz      NOT NULL,
    disbanded_at         timestamptz      NULL,
    CHECK ((seat_lat IS NULL) = (seat_lon IS NULL) AND (seat_lat IS NULL) = (seat_set_at IS NULL)),
    CHECK ((disbanded_at IS NULL) = (member_count >= 1))
);

-- A disbanded sect is history: it never changes again.
CREATE FUNCTION sects.sects_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.disbanded_at IS NOT NULL THEN
        RAISE EXCEPTION 'sect % is disbanded', OLD.id USING ERRCODE = 'restrict_violation';
    END IF;
    IF NEW.version <> OLD.version + 1 OR NEW.normalized_name <> OLD.normalized_name OR NEW.name <> OLD.name THEN
        RAISE EXCEPTION 'illegal sect update' USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER sects_guard BEFORE UPDATE ON sects.sects FOR EACH ROW EXECUTE FUNCTION sects.sects_guard();
CREATE TRIGGER sects_no_delete BEFORE DELETE ON sects.sects FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

CREATE TABLE sects.members (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,  -- one sect per character
    sect_id       uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    account_id    uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    name          text        NOT NULL,
    role          text        NOT NULL CHECK (role IN ('leader', 'elder', 'member')),
    contribution  bigint      NOT NULL CHECK (contribution >= 0),
    joined_at     timestamptz NOT NULL
);
CREATE INDEX members_sect_idx ON sects.members (sect_id);
CREATE UNIQUE INDEX members_one_leader ON sects.members (sect_id) WHERE role = 'leader';

CREATE TABLE sects.applications (
    id              uuid        PRIMARY KEY,
    sect_id         uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    character_id    uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id      uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    character_name  text        NOT NULL,
    message         text        NOT NULL CHECK (length(message) <= 1000),
    status          text        NOT NULL CHECK (status IN ('pending', 'accepted', 'rejected', 'withdrawn')),
    created_at      timestamptz NOT NULL,
    decided_at      timestamptz NULL,
    decided_by      uuid        NULL,
    CHECK ((status = 'pending') = (decided_at IS NULL))
);
CREATE UNIQUE INDEX applications_one_pending ON sects.applications (sect_id, character_id) WHERE status = 'pending';
CREATE INDEX applications_character_idx ON sects.applications (character_id) WHERE status = 'pending';

CREATE TABLE sects.treasuries (
    sect_id     uuid        PRIMARY KEY REFERENCES sects.sects (id) ON DELETE RESTRICT,
    balance     bigint      NOT NULL CHECK (balance >= 0),
    updated_at  timestamptz NOT NULL
);

CREATE TABLE sects.treasury_entries (
    id             uuid        PRIMARY KEY,
    sect_id        uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    delta          bigint      NOT NULL CHECK (delta <> 0),
    balance_after  bigint      NOT NULL CHECK (balance_after >= 0),
    reason         text        NOT NULL CHECK (reason ~ '^[a-z_]{1,64}$'),
    reference_id   text        NOT NULL CHECK (length(reference_id) BETWEEN 1 AND 200),
    created_at     timestamptz NOT NULL,
    UNIQUE (sect_id, reason, reference_id)
);
CREATE INDEX treasury_entries_sect_idx ON sects.treasury_entries (sect_id, id);

CREATE FUNCTION sects.treasury_ledger_apply() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    current_balance bigint;
BEGIN
    INSERT INTO sects.treasuries (sect_id, balance, updated_at) VALUES (NEW.sect_id, 0, NEW.created_at) ON CONFLICT (sect_id) DO NOTHING;
    SELECT balance INTO current_balance FROM sects.treasuries WHERE sect_id = NEW.sect_id FOR UPDATE;
    IF NEW.balance_after <> current_balance + NEW.delta THEN
        RAISE EXCEPTION 'treasury ledger mismatch: % + % <> %', current_balance, NEW.delta, NEW.balance_after USING ERRCODE = 'check_violation';
    END IF;
    PERFORM set_config('jianghu.ledger_write', 'on', true);
    UPDATE sects.treasuries SET balance = NEW.balance_after, updated_at = NEW.created_at WHERE sect_id = NEW.sect_id;
    PERFORM set_config('jianghu.ledger_write', 'off', true);
    RETURN NEW;
END $$;

CREATE TRIGGER treasury_entries_apply BEFORE INSERT ON sects.treasury_entries FOR EACH ROW EXECUTE FUNCTION sects.treasury_ledger_apply();
CREATE TRIGGER treasury_entries_append_only BEFORE UPDATE OR DELETE ON sects.treasury_entries FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();
CREATE TRIGGER treasuries_guard BEFORE UPDATE ON sects.treasuries FOR EACH ROW EXECUTE FUNCTION inventory.projection_guard();

CREATE VIEW sects.treasury_mismatches AS
SELECT t.sect_id, t.balance, coalesce(sum(e.delta), 0) AS ledger_sum
FROM sects.treasuries t LEFT JOIN sects.treasury_entries e ON e.sect_id = t.sect_id
GROUP BY t.sect_id, t.balance
HAVING t.balance <> coalesce(sum(e.delta), 0);

CREATE TABLE sects.events (
    id                    uuid        PRIMARY KEY,
    sect_id               uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    kind                  text        NOT NULL CHECK (kind ~ '^[a-z_]{1,64}$'),
    actor_character_id    uuid        NULL,
    subject_character_id  uuid        NULL,
    data                  jsonb       NOT NULL,
    created_at            timestamptz NOT NULL
);
CREATE INDEX sect_events_sect_idx ON sects.events (sect_id, id);
CREATE TRIGGER sect_events_append_only BEFORE UPDATE OR DELETE ON sects.events FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

-- ------------------------------------------------------------------------------------------------- territory

CREATE SCHEMA territory;

CREATE TABLE territory.territories (
    key              text        PRIMARY KEY CHECK (key ~ '^-?[0-9]{1,8}\.-?[0-9]{1,8}$'),
    owner_sect_id    uuid        NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    claimed_at       timestamptz NULL,
    last_tribute_at  timestamptz NULL,
    version          integer     NOT NULL CHECK (version >= 1),
    CHECK ((owner_sect_id IS NULL) = (claimed_at IS NULL))
);
CREATE INDEX territories_owner_idx ON territory.territories (owner_sect_id) WHERE owner_sect_id IS NOT NULL;

CREATE TABLE territory.wars (
    id                uuid        PRIMARY KEY,
    territory_key     text        NOT NULL REFERENCES territory.territories (key) ON DELETE RESTRICT,
    attacker_sect_id  uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    defender_sect_id  uuid        NOT NULL REFERENCES sects.sects (id) ON DELETE RESTRICT,
    status            text        NOT NULL CHECK (status IN ('declared', 'resolved')),
    declared_at       timestamptz NOT NULL,
    starts_at         timestamptz NOT NULL,
    ends_at           timestamptz NOT NULL,
    attacker_score    integer     NOT NULL CHECK (attacker_score >= 0),
    defender_score    integer     NOT NULL CHECK (defender_score >= 0),
    winner_sect_id    uuid        NULL,
    resolved_at       timestamptz NULL,
    version           integer     NOT NULL CHECK (version >= 1),
    CHECK (attacker_sect_id <> defender_sect_id),
    CHECK (declared_at <= starts_at AND starts_at < ends_at),
    CHECK ((status = 'resolved') = (winner_sect_id IS NOT NULL AND resolved_at IS NOT NULL)),
    CHECK (winner_sect_id IS NULL OR winner_sect_id IN (attacker_sect_id, defender_sect_id))
);
CREATE UNIQUE INDEX wars_one_open_per_territory ON territory.wars (territory_key) WHERE status = 'declared';
CREATE INDEX wars_attacker_idx ON territory.wars (attacker_sect_id) WHERE status = 'declared';
CREATE INDEX wars_defender_idx ON territory.wars (defender_sect_id) WHERE status = 'declared';

CREATE FUNCTION territory.wars_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.status = 'resolved' THEN
        RAISE EXCEPTION 'war % is resolved', OLD.id USING ERRCODE = 'restrict_violation';
    END IF;
    IF NEW.version <> OLD.version + 1 OR NEW.attacker_score < OLD.attacker_score OR NEW.defender_score < OLD.defender_score
       OR NEW.territory_key <> OLD.territory_key OR NEW.attacker_sect_id <> OLD.attacker_sect_id OR NEW.defender_sect_id <> OLD.defender_sect_id
       OR NEW.starts_at <> OLD.starts_at OR NEW.ends_at <> OLD.ends_at THEN
        RAISE EXCEPTION 'illegal war update' USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER wars_guard BEFORE UPDATE ON territory.wars FOR EACH ROW EXECUTE FUNCTION territory.wars_guard();
CREATE TRIGGER wars_no_delete BEFORE DELETE ON territory.wars FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

CREATE TABLE territory.war_battles (
    war_id          uuid        NOT NULL REFERENCES territory.wars (id) ON DELETE RESTRICT,
    battle_id       uuid        NOT NULL,
    winner_sect_id  uuid        NOT NULL,
    counted_at      timestamptz NOT NULL,
    PRIMARY KEY (war_id, battle_id)                     -- a duel counts once per war
);
CREATE TRIGGER war_battles_append_only BEFORE UPDATE OR DELETE ON territory.war_battles FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();

CREATE TABLE territory.ownership_changes (
    id             uuid        PRIMARY KEY,
    territory_key  text        NOT NULL REFERENCES territory.territories (key) ON DELETE RESTRICT,
    from_sect_id   uuid        NULL,
    to_sect_id     uuid        NULL,
    reason         text        NOT NULL CHECK (reason IN ('claim', 'war')),
    war_id         uuid        NULL REFERENCES territory.wars (id) ON DELETE RESTRICT,
    changed_at     timestamptz NOT NULL,
    CHECK ((reason = 'war') = (war_id IS NOT NULL))
);
CREATE INDEX ownership_changes_territory_idx ON territory.ownership_changes (territory_key, id);
CREATE TRIGGER ownership_changes_append_only BEFORE UPDATE OR DELETE ON territory.ownership_changes FOR EACH ROW EXECUTE FUNCTION platform.forbid_modification();
