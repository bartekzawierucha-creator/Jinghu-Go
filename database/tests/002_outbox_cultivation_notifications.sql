-- Stage 3 integrity tests (outbox, cultivation, notifications). Same harness as 001.
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

-- outbox ---------------------------------------------------------------------------------------
INSERT INTO platform.outbox_messages (id, type, payload, occurred_at, available_at) VALUES (gen_random_uuid(), 'characters.character_created', '{}', now(), now());
SELECT pg_temp.expect_error($$INSERT INTO platform.outbox_messages (id, type, payload, occurred_at, available_at) VALUES (gen_random_uuid(), 'Bad Type!', '{}', now(), now())$$, 'event type format');
SELECT pg_temp.expect_error($$UPDATE platform.outbox_messages SET status = 'processed'$$, 'processed requires processed_at');
SELECT pg_temp.expect_error($$UPDATE platform.outbox_messages SET processed_at = now()$$, 'processed_at only with processed status');
SELECT pg_temp.expect_error($$UPDATE platform.outbox_messages SET attempts = -1$$, 'attempts >= 0');
UPDATE platform.outbox_messages SET status = 'processed', processed_at = now();

-- cultivation ----------------------------------------------------------------------------------
INSERT INTO cultivation.states (character_id, account_id, path, realm_index, stage, qi, version, daily_date, daily_meditation_qi, daily_steps, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'qi', 0, 1, 50, 1, current_date, 0, 0, now());
SELECT pg_temp.expect_error($$UPDATE cultivation.states SET qi = -1$$, 'qi >= 0');
SELECT pg_temp.expect_error($$UPDATE cultivation.states SET account_id = '00000000-0000-7000-8000-000000000002'$$, 'state must belong to character owner');
SELECT pg_temp.expect_error($$UPDATE cultivation.states SET path = 'body'$$, 'state path must match character path');
SELECT pg_temp.expect_error($$UPDATE cultivation.states SET stage = 0$$, 'stage >= 1');

INSERT INTO cultivation.breakthroughs (id, character_id, account_id, from_realm, from_stage, to_realm, to_stage, success, chance_ppm, roll, seed, qi_spent, rules_version, occurred_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 0, 1, 0, 2, true, 900000, 123, 42, 100, 'cultivation-1', now());
SELECT pg_temp.expect_error($$INSERT INTO cultivation.breakthroughs (id, character_id, account_id, from_realm, from_stage, to_realm, to_stage, success, chance_ppm, roll, seed, qi_spent, rules_version, occurred_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 0, 1, 0, 2, true, 100, 500000, 42, 100, 'r', now())$$, 'success must equal roll < chance');
SELECT pg_temp.expect_error($$INSERT INTO cultivation.breakthroughs (id, character_id, account_id, from_realm, from_stage, to_realm, to_stage, success, chance_ppm, roll, seed, qi_spent, rules_version, occurred_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 0, 1, 0, 2, false, 100, 500000, 42, 30, 'r', now())$$, 'failure cannot advance');
SELECT pg_temp.expect_error($$UPDATE cultivation.breakthroughs SET success = false$$, 'breakthroughs append-only');
SELECT pg_temp.expect_error($$DELETE FROM cultivation.breakthroughs$$, 'breakthroughs undeletable');

-- notifications --------------------------------------------------------------------------------
INSERT INTO notifications.notifications (id, account_id, type, payload, source_message_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 'welcome', '{}', '00000000-0000-7000-8000-0000000000aa', now());
SELECT pg_temp.expect_error($$INSERT INTO notifications.notifications (id, account_id, type, payload, source_message_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 'welcome', '{}', '00000000-0000-7000-8000-0000000000aa', now())$$, 'one notification per account and source event');
SELECT pg_temp.expect_error($$INSERT INTO notifications.notifications (id, account_id, type, payload, source_message_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 'Bad-Type', '{}', gen_random_uuid(), now())$$, 'notification type format');

ROLLBACK;
\echo 'stage 3 integrity tests: PASSED'
