-- MVP 1.4 integrity tests (Tower of Trials, world boss, sect chat). Same harness as 001.
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
    ('00000000-0000-7000-8000-000000000001', 'a@test.dev', 'h', now());
INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000d1', '00000000-0000-7000-8000-000000000001', 2, 'finalized', 'r', 1, 'b', 'b', 'c',
        '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
        0, now(), now());
INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT '00000000-0000-7000-8000-0000000000c1', account_id, 'Li', 'LI', 'qi', 'r', 'b', 'b', 'c', stats, id, now() FROM characters.drafts;

-- battles: new kinds need their context --------------------------------------------------------
CREATE FUNCTION pg_temp.battle(kind text, ctx text, reward bigint, status text) RETURNS text LANGUAGE sql AS $$
    SELECT format($q$INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, npc_id, combatant_a, combatant_b, state,
        version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at, finished_at, context)
        VALUES (gen_random_uuid(), %L, %L, 'combat-3', 1, repeat('a', 64), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001',
        'grey_wolf', '{}', '{}', '{}', 1, now(), 0, 0, %s, now(), now(), %s, %L)$q$,
        kind, status, reward, CASE WHEN status = 'finished' THEN 'now()' ELSE 'NULL' END, ctx)
$$;
DO $$ BEGIN EXECUTE pg_temp.battle('tower', 'tower:3', 60, 'finished'); EXECUTE pg_temp.battle('boss', 'boss:41', 0, 'finished'); END $$;
SELECT pg_temp.expect_error(pg_temp.battle('tower', NULL, 0, 'finished'), 'tower battle needs a floor');
SELECT pg_temp.expect_error(pg_temp.battle('boss', 'tower:3', 0, 'finished'), 'boss battle needs a boss context');
SELECT pg_temp.expect_error(pg_temp.battle('pve', 'tower:3', 0, 'finished'), 'pve battles have no context');
SELECT pg_temp.expect_error(pg_temp.battle('tower', 'tower:x', 0, 'finished'), 'context format');
SELECT pg_temp.expect_error(pg_temp.battle('duel', NULL, 0, 'finished'), 'unknown kind');
SELECT pg_temp.expect_error(pg_temp.battle('tower', 'tower:4', 10, 'active'), 'rewards only on finished battles');

-- tower progress -------------------------------------------------------------------------------
INSERT INTO combat.tower_progress VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 3, now(), 1);
SELECT pg_temp.expect_error($$UPDATE combat.tower_progress SET best_floor = -1$$, 'floor >= 0');
SELECT pg_temp.expect_error($$INSERT INTO combat.tower_progress VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 4, now(), 1)$$, 'one row per character');

-- world boss -----------------------------------------------------------------------------------
INSERT INTO combat.world_bosses VALUES (41, 'storm_qilin', 20000, 20000, NULL, 1);
SELECT pg_temp.expect_error($$UPDATE combat.world_bosses SET hp_left = 20001$$, 'hp left <= pool');
SELECT pg_temp.expect_error($$UPDATE combat.world_bosses SET hp_left = 0$$, 'a dead boss has a defeat time');
SELECT pg_temp.expect_error($$UPDATE combat.world_bosses SET defeated_at = now()$$, 'a living boss has none');
UPDATE combat.world_bosses SET hp_left = 0, defeated_at = now();
INSERT INTO combat.world_boss_damage VALUES (41, '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 900, 2, NULL, 1);
SELECT pg_temp.expect_error($$UPDATE combat.world_boss_damage SET damage = -1$$, 'damage >= 0');
SELECT pg_temp.expect_error($$INSERT INTO combat.world_boss_damage VALUES (42, '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 1, 1, NULL, 1)$$, 'damage needs its boss');

-- a deleted character takes their tower record and boss tally along
DELETE FROM combat.battles WHERE character_a = '00000000-0000-7000-8000-0000000000c1';
DELETE FROM characters.characters WHERE id = '00000000-0000-7000-8000-0000000000c1';
SELECT pg_temp.expect((SELECT count(*) FROM combat.tower_progress) + (SELECT count(*) FROM combat.world_boss_damage) = 0, 'cascade on character delete');
INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT '00000000-0000-7000-8000-0000000000c1', account_id, 'Li', 'LI', 'qi', 'r', 'b', 'b', 'c', stats, id, now() FROM characters.drafts;

-- sect chat ------------------------------------------------------------------------------------
INSERT INTO sects.sects (id, name, normalized_name, leader_character_id, leader_account_id, member_count, version, created_at)
VALUES ('00000000-0000-7000-8000-0000000000e1', 'Lotos', 'LOTOS', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 1, 1, now());
INSERT INTO sects.chat_messages VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000e1', '00000000-0000-7000-8000-0000000000c1', 'Li', 'Witajcie!', now());
SELECT pg_temp.expect_error($$INSERT INTO sects.chat_messages VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000e1', '00000000-0000-7000-8000-0000000000c1', 'Li', '', now())$$, 'empty message');
SELECT pg_temp.expect_error($$INSERT INTO sects.chat_messages VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000e1', '00000000-0000-7000-8000-0000000000c1', 'Li', repeat('x', 301), now())$$, 'too long');
SELECT pg_temp.expect_error($$INSERT INTO sects.chat_messages VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000e1', '00000000-0000-7000-8000-0000000000c1', 'Li', E'a\nb', now())$$, 'no control characters');

ROLLBACK;
\echo 'trials and chat integrity tests: PASSED'
