-- Stage 7 integrity tests (sects, treasury, applications, territories, wars). Same harness as 001.
\set ON_ERROR_STOP 1
SET client_min_messages = warning;
BEGIN;

CREATE FUNCTION pg_temp.expect_error(stmt text, label text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    BEGIN
        EXECUTE stmt;
    EXCEPTION WHEN OTHERS THEN
        RETURN;
    END;
    RAISE EXCEPTION 'TEST FAILED (expected an error): %', label;
END $$;

CREATE FUNCTION pg_temp.expect(cond boolean, label text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    IF cond IS NOT TRUE THEN RAISE EXCEPTION 'TEST FAILED: %', label; END IF;
END $$;

INSERT INTO identity.accounts (id, email, password_hash, created_at) VALUES
    ('00000000-0000-7000-8000-000000000001', 'a@test.dev', 'h', now()),
    ('00000000-0000-7000-8000-000000000002', 'b@test.dev', 'h', now());
INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000d1', '00000000-0000-7000-8000-000000000001', 2, 'finalized', 'r', 1, 'b', 'b', 'c',
        '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
        0, now(), now());
INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT '00000000-0000-7000-8000-0000000000c1', account_id, 'Li', 'LI', 'qi', 'r', 'b', 'b', 'c', stats, id, now() FROM characters.drafts;

-- sects ------------------------------------------------------------------------------------------
INSERT INTO sects.sects (id, name, normalized_name, leader_character_id, leader_account_id, member_count, version, created_at)
VALUES ('00000000-0000-7000-8000-00000000005a', 'Wudang', 'WUDANG', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 1, 1, now());
SELECT pg_temp.expect_error($$INSERT INTO sects.sects (id, name, normalized_name, leader_character_id, leader_account_id, member_count, version, created_at)
    VALUES (gen_random_uuid(), 'wudang', 'WUDANG', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 1, 1, now())$$, 'sect names unique');
INSERT INTO sects.members (character_id, sect_id, account_id, name, role, contribution, joined_at)
VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-00000000005a', '00000000-0000-7000-8000-000000000001', 'Li', 'leader', 0, now());
SELECT pg_temp.expect_error($$INSERT INTO sects.members (character_id, sect_id, account_id, name, role, contribution, joined_at)
    VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-00000000005a', '00000000-0000-7000-8000-000000000001', 'Li', 'member', 0, now())$$, 'one sect per character');
SELECT pg_temp.expect_error($$UPDATE sects.members SET role = 'emperor'$$, 'known roles only');
SELECT pg_temp.expect_error($$UPDATE sects.members SET contribution = -1$$, 'contribution >= 0');
SELECT pg_temp.expect_error($$UPDATE sects.sects SET member_count = 2$$, 'every sect change bumps the version');
SELECT pg_temp.expect_error($$UPDATE sects.sects SET name = 'Inna', version = 2$$, 'sect name is permanent');
SELECT pg_temp.expect_error($$UPDATE sects.sects SET seat_lat = 50.0, version = 2$$, 'seat coordinates come together with their time');
SELECT pg_temp.expect_error($$DELETE FROM sects.sects$$, 'sects are never deleted');

INSERT INTO sects.treasury_entries (id, sect_id, delta, balance_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000005a', 300, 300, 'donation', 'd1', now());
SELECT pg_temp.expect_error($$INSERT INTO sects.treasury_entries (id, sect_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000005a', -400, -100, 'seat', 's1', now())$$, 'treasury never negative');
SELECT pg_temp.expect_error($$UPDATE sects.treasuries SET balance = 99999$$, 'treasury changes only through its ledger');
SELECT pg_temp.expect((SELECT count(*) FROM sects.treasury_mismatches) = 0, 'treasury reconciles');

INSERT INTO sects.applications (id, sect_id, character_id, account_id, character_name, message, status, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000005a', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', '', 'pending', now());
SELECT pg_temp.expect_error($$INSERT INTO sects.applications (id, sect_id, character_id, account_id, character_name, message, status, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000005a', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', '', 'pending', now())$$, 'one pending application per sect and character');

INSERT INTO sects.events (id, sect_id, kind, data, created_at) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000005a', 'founded', '{}', now());
SELECT pg_temp.expect_error($$UPDATE sects.events SET kind = 'kicked'$$, 'sect history append-only');

-- territory ----------------------------------------------------------------------------------------
INSERT INTO sects.sects (id, name, normalized_name, leader_character_id, leader_account_id, member_count, version, created_at)
VALUES ('00000000-0000-7000-8000-00000000005b', 'Emei', 'EMEI', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 1, 1, now());
INSERT INTO territory.territories (key, owner_sect_id, claimed_at, version) VALUES ('398.1001', '00000000-0000-7000-8000-00000000005a', now(), 1);
SELECT pg_temp.expect_error($$INSERT INTO territory.territories (key, version) VALUES ('Krakow', 1)$$, 'territory key format');
INSERT INTO territory.wars (id, territory_key, attacker_sect_id, defender_sect_id, status, declared_at, starts_at, ends_at, attacker_score, defender_score, version)
VALUES ('00000000-0000-7000-8000-0000000000aa', '398.1001', '00000000-0000-7000-8000-00000000005b', '00000000-0000-7000-8000-00000000005a', 'declared',
        now(), now() + interval '12 hours', now() + interval '36 hours', 0, 0, 1);
SELECT pg_temp.expect_error($$INSERT INTO territory.wars (id, territory_key, attacker_sect_id, defender_sect_id, status, declared_at, starts_at, ends_at, attacker_score, defender_score, version)
    VALUES (gen_random_uuid(), '398.1001', '00000000-0000-7000-8000-00000000005b', '00000000-0000-7000-8000-00000000005a', 'declared', now(), now(), now() + interval '1 hour', 0, 0, 1)$$, 'one open war per territory');
SELECT pg_temp.expect_error($$UPDATE territory.wars SET attacker_score = 5$$, 'war changes bump the version');
SELECT pg_temp.expect_error($$UPDATE territory.wars SET defender_score = 0, attacker_score = 0, ends_at = now(), version = 2$$, 'war window is fixed');
SELECT pg_temp.expect_error($$UPDATE territory.wars SET status = 'resolved', version = 2$$, 'resolved wars have a winner');
SELECT pg_temp.expect_error($$UPDATE territory.wars SET status = 'resolved', winner_sect_id = gen_random_uuid(), resolved_at = now(), version = 2$$, 'winner is one of the two sects');
UPDATE territory.wars SET attacker_score = 2, version = 2;
SELECT pg_temp.expect_error($$UPDATE territory.wars SET attacker_score = 1, version = 3$$, 'scores never go down');
UPDATE territory.wars SET status = 'resolved', winner_sect_id = attacker_sect_id, resolved_at = now(), version = 3;
SELECT pg_temp.expect_error($$UPDATE territory.wars SET defender_score = 9, version = 4$$, 'resolved wars are final');

INSERT INTO territory.war_battles (war_id, battle_id, winner_sect_id, counted_at)
VALUES ('00000000-0000-7000-8000-0000000000aa', '00000000-0000-7000-8000-0000000000bb', '00000000-0000-7000-8000-00000000005b', now());
SELECT pg_temp.expect_error($$INSERT INTO territory.war_battles (war_id, battle_id, winner_sect_id, counted_at)
    VALUES ('00000000-0000-7000-8000-0000000000aa', '00000000-0000-7000-8000-0000000000bb', '00000000-0000-7000-8000-00000000005b', now())$$, 'a duel counts once per war');

INSERT INTO territory.ownership_changes (id, territory_key, from_sect_id, to_sect_id, reason, war_id, changed_at)
VALUES (gen_random_uuid(), '398.1001', '00000000-0000-7000-8000-00000000005a', '00000000-0000-7000-8000-00000000005b', 'war', '00000000-0000-7000-8000-0000000000aa', now());
SELECT pg_temp.expect_error($$INSERT INTO territory.ownership_changes (id, territory_key, to_sect_id, reason, changed_at)
    VALUES (gen_random_uuid(), '398.1001', '00000000-0000-7000-8000-00000000005b', 'war', now())$$, 'war transfers name their war');
SELECT pg_temp.expect_error($$DELETE FROM territory.ownership_changes$$, 'ownership history append-only');

ROLLBACK;
\echo 'sects and territory integrity tests: PASSED'
