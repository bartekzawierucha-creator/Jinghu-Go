-- MVP 1.5 integrity tests (cultivation-3, tribulation/arena/spirit battles, arts, estate, crafting queue, titles, sect development).
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

-- cultivation-3: 12 stages, 66 realms; profiles ------------------------------------------------
INSERT INTO cultivation.states (character_id, account_id, path, realm_index, stage, qi, version, deviation_until, meditation_started_at, daily_date, daily_meditation_qi, daily_steps, last_steps_report_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'qi', 0, 12, 0, 1, NULL, NULL, current_date, 0, 0, NULL, now());
SELECT pg_temp.expect_error($$UPDATE cultivation.states SET stage = 13$$, 'at most 12 stages');
SELECT pg_temp.expect_error($$UPDATE cultivation.states SET realm_index = 66$$, 'realm index 0-65');
UPDATE cultivation.states SET realm_index = 65;
INSERT INTO cultivation.profiles VALUES ('00000000-0000-7000-8000-0000000000c1', '{"mode":"idle"}', 1, now());
SELECT pg_temp.expect_error($$UPDATE cultivation.profiles SET version = 0$$, 'profile version >= 1');
SELECT pg_temp.expect_error($$INSERT INTO cultivation.profiles VALUES ('00000000-0000-7000-8000-0000000000c1', '{}', 1, now())$$, 'one profile per character');

-- battles: tribulation, arena and spirit kinds ------------------------------------------------
CREATE FUNCTION pg_temp.battle(kind text, ctx text, reward bigint, status text) RETURNS text LANGUAGE sql AS $$
    SELECT format($q$INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, npc_id, combatant_a, combatant_b, state,
        version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at, finished_at, context)
        VALUES (gen_random_uuid(), %L, %L, 'combat-4', 1, repeat('a', 64), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001',
        'heavenly_tribulation', '{}', '{}', '{}', 1, now(), 0, 0, %s, now(), now(), %s, %L)$q$,
        kind, status, reward, CASE WHEN status = 'finished' THEN 'now()' ELSE 'NULL' END, ctx)
$$;
DO $$ BEGIN
    EXECUTE pg_temp.battle('tribulation', 'trib:12', 0, 'finished');
    EXECUTE pg_temp.battle('arena', 'arena:00000000-0000-7000-8000-0000000000c1', 60, 'finished');
    EXECUTE pg_temp.battle('spirit', 'spirit:3f1e2d3c-1111-4c4c-9a9a-123456789abc', 0, 'finished');
END $$;
SELECT pg_temp.expect_error(pg_temp.battle('tribulation', 'trib:12', 100, 'finished'), 'tribulations pay no Qi');
SELECT pg_temp.expect_error(pg_temp.battle('spirit', 'trib:3', 0, 'finished'), 'spirit battles need a spirit context');
SELECT pg_temp.expect_error(pg_temp.battle('arena', NULL, 0, 'finished'), 'arena battles need the opponent');
SELECT pg_temp.expect_error(pg_temp.battle('spirit', 'spirit:XYZ', 0, 'finished'), 'context format');

-- arena entries and weekly claims ---------------------------------------------------------------
INSERT INTO combat.arena_entries VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 1000, 0, 0, '{}', now(), 1);
SELECT pg_temp.expect_error($$UPDATE combat.arena_entries SET rating = -1$$, 'rating >= 0');
SELECT pg_temp.expect_error($$UPDATE combat.arena_entries SET wins = -1$$, 'wins >= 0');
INSERT INTO combat.arena_claims VALUES ('00000000-0000-7000-8000-0000000000c1', '2026-W42', now());
SELECT pg_temp.expect_error($$INSERT INTO combat.arena_claims VALUES ('00000000-0000-7000-8000-0000000000c1', '2026-W42', now())$$, 'one claim per week');
SELECT pg_temp.expect_error($$INSERT INTO combat.arena_claims VALUES ('00000000-0000-7000-8000-0000000000c1', 'week-42', now())$$, 'ISO week format');

-- arts, estate, sect development: versioned documents; event guards ---------------------------
INSERT INTO arts.profiles VALUES ('00000000-0000-7000-8000-0000000000c1', '{}', 1, now());
SELECT pg_temp.expect_error($$INSERT INTO arts.profiles VALUES ('00000000-0000-7000-8000-0000000000c1', '{}', 1, now())$$, 'one arts profile per character');
SELECT pg_temp.expect_error($$UPDATE arts.profiles SET version = 0$$, 'arts version >= 1');
INSERT INTO arts.handled_events VALUES ('00000000-0000-7000-8000-00000000ee01', '00000000-0000-7000-8000-0000000000c1', now());
SELECT pg_temp.expect_error($$INSERT INTO arts.handled_events VALUES ('00000000-0000-7000-8000-00000000ee01', '00000000-0000-7000-8000-0000000000c1', now())$$, 'an event is applied once');
INSERT INTO estate.profiles VALUES ('00000000-0000-7000-8000-0000000000c1', '{}', 1, now());
SELECT pg_temp.expect_error($$UPDATE estate.profiles SET version = 0$$, 'estate version >= 1');

-- crafting queue: one batch per workshop, sane values -----------------------------------------
INSERT INTO crafting.queue VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'brew_qi_pill', 'alchemy', 5, 850, 42, 25, now(), now() + interval '30 minutes');
SELECT pg_temp.expect_error($$INSERT INTO crafting.queue VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'brew_qi_pill', 'alchemy', 1, 850, 42, 5, now(), now())$$, 'one batch per workshop');
SELECT pg_temp.expect_error($$INSERT INTO crafting.queue VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'forge_x', 'forge', 1, 1001, 1, 0, now(), now())$$, 'chance <= 1000');
SELECT pg_temp.expect_error($$INSERT INTO crafting.queue VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'forge_x', 'forge', 1, 500, 1, 0, now(), now() - interval '1 minute')$$, 'ready after start');
SELECT pg_temp.expect_error($$INSERT INTO crafting.queue VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'forge_x', 'magic', 1, 500, 1, 0, now(), now())$$, 'known workshop');

-- titles, seasonal progress ---------------------------------------------------------------------
INSERT INTO quests.selected_titles VALUES ('00000000-0000-7000-8000-0000000000c1', 'realm_body_tempering', now());
SELECT pg_temp.expect_error($$UPDATE quests.selected_titles SET title_id = 'Bad Title!'$$, 'title id format');
INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', 'S2026', 'm_battles_won', 3);
SELECT pg_temp.expect_error($$INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ('00000000-0000-7000-8000-0000000000c1', 'season', 'm_x', 1)$$, 'period format');

-- deleting the character removes its documents ---------------------------------------------------
DELETE FROM combat.battles;
DELETE FROM crafting.queue;
DELETE FROM characters.characters WHERE id = '00000000-0000-7000-8000-0000000000c1';
SELECT pg_temp.expect((SELECT count(*) FROM arts.profiles) + (SELECT count(*) FROM estate.profiles) + (SELECT count(*) FROM cultivation.profiles)
    + (SELECT count(*) FROM quests.selected_titles) + (SELECT count(*) FROM combat.arena_entries) = 0, 'documents cascade with the character');

ROLLBACK;
\echo 'MVP 1.5 integrity tests passed'
