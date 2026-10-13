-- MVP 1.3 integrity tests (idle hunts). Same harness as 001.
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

-- idle hunts -------------------------------------------------------------------------------------
INSERT INTO combat.idle_hunts (character_id, account_id, npc_id, started_at, settled_at, battles, wins, losses, qi, version)
VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'grey_wolf', now(), now(), 0, 0, 0, 0, 1);
SELECT pg_temp.expect_error($$INSERT INTO combat.idle_hunts (character_id, account_id, npc_id, started_at, settled_at, battles, wins, losses, qi, version)
    VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'fire_fox', now(), now(), 0, 0, 0, 0, 1)$$, 'one hunt per character');
SELECT pg_temp.expect_error($$UPDATE combat.idle_hunts SET wins = 3, battles = 2$$, 'wins + losses <= battles');
SELECT pg_temp.expect_error($$UPDATE combat.idle_hunts SET settled_at = started_at - interval '1 minute'$$, 'settled after start');
SELECT pg_temp.expect_error($$UPDATE combat.idle_hunts SET qi = -1$$, 'qi >= 0');
SELECT pg_temp.expect_error($$UPDATE combat.idle_hunts SET npc_id = 'Wolf!'$$, 'npc id format');
DELETE FROM characters.characters WHERE id = '00000000-0000-7000-8000-0000000000c1';
SELECT pg_temp.expect((SELECT count(*) FROM combat.idle_hunts) = 0, 'cascade on character delete');

ROLLBACK;
\echo 'idle hunt integrity tests: PASSED'
