-- Stage 4 integrity tests (combat, reward grants). Same harness as 001.
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

INSERT INTO identity.accounts (id, email, password_hash, created_at) VALUES
    ('00000000-0000-7000-8000-000000000001', 'a@test.dev', 'h', now()),
    ('00000000-0000-7000-8000-000000000002', 'b@test.dev', 'h', now());
INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
SELECT d.id, d.acc, 2, 'finalized', 'r', 1, 'b', 'b', 'c',
       '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
       0, now(), now()
FROM (VALUES ('00000000-0000-7000-8000-0000000000d1'::uuid, '00000000-0000-7000-8000-000000000001'::uuid),
             ('00000000-0000-7000-8000-0000000000d2'::uuid, '00000000-0000-7000-8000-000000000002'::uuid)) AS d(id, acc);
INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT ('00000000-0000-7000-8000-0000000000c' || right(id::text, 1))::uuid, account_id, 'N' || right(id::text, 1), 'N' || right(id::text, 1), 'qi', 'r', 'b', 'b', 'c', stats, id, now()
FROM characters.drafts;

-- loadouts & mastery
SELECT pg_temp.expect_error($$INSERT INTO combat.loadouts VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', '["a"]', 1)$$, 'loadout has 8 slots');
SELECT pg_temp.expect_error($$INSERT INTO combat.technique_mastery VALUES ('00000000-0000-7000-8000-0000000000c1', 'palm_strike', 11, 0)$$, 'technique level <= 10');
SELECT pg_temp.expect_error($$INSERT INTO combat.technique_mastery VALUES ('00000000-0000-7000-8000-0000000000c1', 'palm_strike', 1, -1)$$, 'xp >= 0');

-- battles
INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, npc_id, combatant_a, combatant_b, state, version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000b1', 'pve', 'active', 'combat-1', 1, repeat('a', 64), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001',
        'grey_wolf', '{}', '{}', '{}', 1, now(), 0, 0, 0, now(), now());
SELECT pg_temp.expect_error($$INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, npc_id, combatant_a, combatant_b, state, version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at)
    VALUES (gen_random_uuid(), 'pve', 'active', 'combat-1', 1, repeat('a', 64), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001',
            'grey_wolf', '{}', '{}', '{}', 1, now(), 0, 0, 0, now(), now())$$, 'one active battle per character');
SELECT pg_temp.expect_error($$INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, character_b, account_b, version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at)
    VALUES (gen_random_uuid(), 'pvp', 'pending_acceptance', 'combat-1', 1, repeat('a', 64), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001',
            '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 1, now(), 0, 0, 0, now(), now())$$, 'cannot fight yourself');
SELECT pg_temp.expect_error($$INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at)
    VALUES (gen_random_uuid(), 'pve', 'pending_acceptance', 'combat-1', 1, repeat('a', 64), '00000000-0000-7000-8000-0000000000c2', '00000000-0000-7000-8000-000000000002', 1, now(), 0, 0, 0, now(), now())$$, 'pve needs an npc');
SELECT pg_temp.expect_error($$UPDATE combat.battles SET reward_qi = 10$$, 'rewards only on finished battles');
SELECT pg_temp.expect_error($$UPDATE combat.battles SET status = 'finished'$$, 'finished requires finished_at');
SELECT pg_temp.expect_error($$UPDATE combat.battles SET state = NULL$$, 'active battle has state');

-- intents & turns
INSERT INTO combat.intents VALUES ('00000000-0000-7000-8000-0000000000b1', 1, 'a', '{}', now());
SELECT pg_temp.expect_error($$INSERT INTO combat.intents VALUES ('00000000-0000-7000-8000-0000000000b1', 1, 'a', '{}', now())$$, 'one action per side per turn');
SELECT pg_temp.expect_error($$INSERT INTO combat.intents VALUES ('00000000-0000-7000-8000-0000000000b1', 1, 'c', '{}', now())$$, 'side a or b');
INSERT INTO combat.turns VALUES ('00000000-0000-7000-8000-0000000000b1', 1, '{}', '{}', false, false, '[]', '{}', now());
SELECT pg_temp.expect_error($$INSERT INTO combat.turns VALUES ('00000000-0000-7000-8000-0000000000b1', 1, '{}', '{}', false, false, '[]', '{}', now())$$, 'turn resolves once');
SELECT pg_temp.expect_error($$UPDATE combat.turns SET forced_a = true$$, 'turn log append-only');
SELECT pg_temp.expect_error($$DELETE FROM combat.turns$$, 'turn log undeletable');

-- reward grants
INSERT INTO cultivation.reward_grants VALUES ('00000000-0000-7000-8000-0000000000ee', '00000000-0000-7000-8000-0000000000c1', 40, now());
SELECT pg_temp.expect_error($$INSERT INTO cultivation.reward_grants VALUES ('00000000-0000-7000-8000-0000000000ee', '00000000-0000-7000-8000-0000000000c1', 40, now())$$, 'one grant per event and character');
SELECT pg_temp.expect_error($$INSERT INTO cultivation.reward_grants VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 0, now())$$, 'grant must be positive');

ROLLBACK;
\echo 'stage 4 integrity tests: PASSED'
