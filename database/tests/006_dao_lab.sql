-- Dao Lab integrity tests (designs, knowledge, scrolls). Same harness as 001.
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

-- Dao Lab --------------------------------------------------------------------------------------
INSERT INTO combat.custom_techniques (id, author_character_id, author_account_id, author_name, slot, definition, points, allowance, rules_version, created_at)
VALUES ('dao_abcdefghijklmnop', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'attack',
        '{"id":"dao_abcdefghijklmnop","slot":"attack","qiCost":6}', 250, 292, 'dao-1', now());
SELECT pg_temp.expect_error($$INSERT INTO combat.custom_techniques (id, author_character_id, author_account_id, author_name, slot, definition, points, allowance, rules_version, created_at)
    VALUES ('dao_zzzzzzzzzzzzzzzz', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'attack', '{"id":"dao_zzzzzzzzzzzzzzzz","slot":"attack"}', 1, 2, 'dao-1', now())$$, 'id format');
SELECT pg_temp.expect_error($$INSERT INTO combat.custom_techniques (id, author_character_id, author_account_id, author_name, slot, definition, points, allowance, rules_version, created_at)
    VALUES ('dao_aaaaaaaaaaaaaaaa', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'attack', '{"id":"dao_aaaaaaaaaaaaaaaa","slot":"attack"}', 300, 200, 'dao-1', now())$$, 'over-budget designs are never stored');
SELECT pg_temp.expect_error($$INSERT INTO combat.custom_techniques (id, author_character_id, author_account_id, author_name, slot, definition, points, allowance, rules_version, created_at)
    VALUES ('dao_bbbbbbbbbbbbbbbb', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'attack', '{"id":"dao_abcdefghijklmnop","slot":"attack"}', 1, 2, 'dao-1', now())$$, 'definition must describe this technique');
SELECT pg_temp.expect_error($$UPDATE combat.custom_techniques SET points = 0$$, 'designs are immutable');
SELECT pg_temp.expect_error($$DELETE FROM combat.custom_techniques$$, 'designs are never deleted');

INSERT INTO combat.known_techniques (character_id, technique_id, source, learned_at) VALUES ('00000000-0000-7000-8000-0000000000c1', 'dao_abcdefghijklmnop', 'designed', now());
SELECT pg_temp.expect_error($$INSERT INTO combat.known_techniques (character_id, technique_id, source, learned_at) VALUES ('00000000-0000-7000-8000-0000000000c1', 'dao_abcdefghijklmnop', 'scroll', now())$$, 'known once per character');
SELECT pg_temp.expect_error($$INSERT INTO combat.known_techniques (character_id, technique_id, source, learned_at) VALUES ('00000000-0000-7000-8000-0000000000c1', 'dao_cccccccccccccccc', 'scroll', now())$$, 'only existing designs can be known');

-- scrolls carry an immutable reference
INSERT INTO inventory.items (id, character_id, account_id, item_id, upgrade_level, state, version, created_at, bound_ref)
VALUES ('00000000-0000-7000-8000-0000000000e9', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'technique_scroll', 0, 'bag', 1, now(), 'dao_abcdefghijklmnop');
SELECT pg_temp.expect_error($$UPDATE inventory.items SET bound_ref = 'dao_pppppppppppppppp', version = 2 WHERE id = '00000000-0000-7000-8000-0000000000e9'$$, 'scroll reference immutable');
UPDATE inventory.items SET state = 'destroyed', version = 2 WHERE id = '00000000-0000-7000-8000-0000000000e9';
SELECT pg_temp.expect_error($$UPDATE inventory.items SET state = 'bag', version = 3 WHERE id = '00000000-0000-7000-8000-0000000000e9'$$, 'a consumed scroll stays consumed');

ROLLBACK;
\echo 'dao lab integrity tests: PASSED'
