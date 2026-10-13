-- MVP 1.1 integrity tests (spirit pets, meridians, quests, check-ins). Same harness as 001.
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

-- spirit pets -------------------------------------------------------------------------------------
INSERT INTO combat.spirit_pets (character_id, npc_id, xp, tamed_in_battle, tamed_at)
VALUES ('00000000-0000-7000-8000-0000000000c1', 'grey_wolf', 0, gen_random_uuid(), now());
SELECT pg_temp.expect_error($$INSERT INTO combat.spirit_pets (character_id, npc_id, xp, tamed_in_battle, tamed_at)
    VALUES ('00000000-0000-7000-8000-0000000000c1', 'fire_fox', 0, gen_random_uuid(), now())$$, 'one pet per character');
SELECT pg_temp.expect_error($$UPDATE combat.spirit_pets SET xp = -1$$, 'pet xp >= 0');
SELECT pg_temp.expect_error($$UPDATE combat.spirit_pets SET npc_id = 'Wolf!'$$, 'npc id format');

-- meridians ---------------------------------------------------------------------------------------
INSERT INTO cultivation.meridians (character_id, stat, points) VALUES ('00000000-0000-7000-8000-0000000000c1', 'strength', 2);
SELECT pg_temp.expect_error($$INSERT INTO cultivation.meridians (character_id, stat, points) VALUES ('00000000-0000-7000-8000-0000000000c1', 'luck', 1)$$, 'known stats only');
SELECT pg_temp.expect_error($$INSERT INTO cultivation.meridians (character_id, stat, points) VALUES ('00000000-0000-7000-8000-0000000000c1', 'strength', 1)$$, 'one row per stat');
SELECT pg_temp.expect_error($$UPDATE cultivation.meridians SET points = 0$$, 'points > 0 (closed meridians are deleted)');

-- quests ------------------------------------------------------------------------------------------
INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', '2026-10-13', 'm_battles_won', 1);
INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', 'life', 'm_battles_won', 1);
SELECT pg_temp.expect_error($$INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', 'week', 'm_x', 1)$$, 'period is a day or life');
SELECT pg_temp.expect_error($$INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', 'life', 'Bad-Id', 1)$$, 'quest id format');
SELECT pg_temp.expect_error($$UPDATE quests.progress SET progress = -1$$, 'progress >= 0');
SELECT pg_temp.expect_error($$INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', 'life', 'm_battles_won', 5)$$, 'one row per quest and period');

INSERT INTO quests.counted_events (character_id, message_id, counted_at) VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-0000000000e1', now());
SELECT pg_temp.expect_error($$INSERT INTO quests.counted_events (character_id, message_id, counted_at) VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-0000000000e1', now())$$, 'an event counts once');

INSERT INTO quests.checkins (character_id, last_day, streak, best_streak, updated_at) VALUES ('00000000-0000-7000-8000-0000000000c1', '2026-10-13', 2, 5, now());
SELECT pg_temp.expect_error($$UPDATE quests.checkins SET streak = 0$$, 'streak >= 1');
SELECT pg_temp.expect_error($$UPDATE quests.checkins SET best_streak = 1$$, 'best streak >= streak');

-- deleting a character removes its progression rows
DELETE FROM characters.characters WHERE id = '00000000-0000-7000-8000-0000000000c1';
SELECT pg_temp.expect((SELECT count(*) FROM combat.spirit_pets) + (SELECT count(*) FROM cultivation.meridians) + (SELECT count(*) FROM quests.progress)
    + (SELECT count(*) FROM quests.counted_events) + (SELECT count(*) FROM quests.checkins) = 0, 'cascade on character delete');

ROLLBACK;
\echo 'progression integrity tests: PASSED'
