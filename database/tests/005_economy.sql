-- Stage 6 integrity tests (inventory ledgers, unique items, market, crafting log). Same harness as 001.
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

-- inventory ledgers ----------------------------------------------------------------------------
INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 100, 100, 'initial_grant', 'c1', now());
SELECT pg_temp.expect((SELECT balance FROM inventory.wallets WHERE character_id = '00000000-0000-7000-8000-0000000000c1') = 100, 'trigger maintains balance');
SELECT pg_temp.expect_error($$INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', -30, 80, 'market_fee', 'x', now())$$, 'balance_after must equal balance + delta');
SELECT pg_temp.expect_error($$INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', -130, -30, 'market_fee', 'x', now())$$, 'balance can never go negative');
SELECT pg_temp.expect_error($$INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 0, 100, 'noop', 'x', now())$$, 'zero movements are refused');
SELECT pg_temp.expect_error($$INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 5, 105, 'initial_grant', 'c1', now())$$, 'same operation cannot be applied twice');
SELECT pg_temp.expect_error($$UPDATE inventory.wallets SET balance = 1000000$$, 'balances change only through the ledger');
SELECT pg_temp.expect_error($$UPDATE inventory.wallet_entries SET delta = 1000$$, 'wallet ledger append-only');
SELECT pg_temp.expect_error($$DELETE FROM inventory.wallet_entries$$, 'wallet ledger undeletable');

INSERT INTO inventory.stack_entries (id, character_id, item_id, delta, quantity_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 'iron_ore', 5, 5, 'gather', 'g1', now());
INSERT INTO inventory.stack_entries (id, character_id, item_id, delta, quantity_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 'iron_ore', -5, 0, 'craft', 'k1', now());
SELECT pg_temp.expect_error($$INSERT INTO inventory.stack_entries (id, character_id, item_id, delta, quantity_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 'iron_ore', -1, -1, 'craft', 'k2', now())$$, 'stack cannot go negative');
SELECT pg_temp.expect_error($$UPDATE inventory.stacks SET quantity = 999$$, 'stacks change only through the ledger');
SELECT pg_temp.expect((SELECT count(*) FROM inventory.wallet_mismatches) = 0 AND (SELECT count(*) FROM inventory.stack_mismatches) = 0, 'reconciliation clean');

-- unique items and market -----------------------------------------------------------------------
INSERT INTO inventory.items (id, character_id, account_id, item_id, upgrade_level, state, version, created_at)
VALUES ('00000000-0000-7000-8000-0000000000e1', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'iron_sword', 0, 'bag', 1, now());
SELECT pg_temp.expect_error($$UPDATE inventory.items SET account_id = '00000000-0000-7000-8000-000000000002', version = 2$$, 'owner must match character account');
SELECT pg_temp.expect_error($$UPDATE inventory.items SET upgrade_level = 3$$, 'every item change bumps the version by one');
SELECT pg_temp.expect_error($$UPDATE inventory.items SET item_id = 'phoenix_feather', version = 2$$, 'an item never changes kind');
UPDATE inventory.items SET state = 'listed', version = 2;

INSERT INTO market.listings (id, seller_character_id, seller_account_id, seller_name, item_id, quantity, instance_id, upgrade_level, price, fee, status, tax, version, created_at, expires_at)
VALUES ('00000000-0000-7000-8000-0000000000f1', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'iron_sword', 1,
        '00000000-0000-7000-8000-0000000000e1', 0, 100, 1, 'active', 0, 1, now(), now() + interval '48 hours');
SELECT pg_temp.expect_error($$INSERT INTO market.listings (id, seller_character_id, seller_account_id, seller_name, item_id, quantity, instance_id, upgrade_level, price, fee, status, tax, version, created_at, expires_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'iron_sword', 1,
            '00000000-0000-7000-8000-0000000000e1', 0, 100, 1, 'active', 0, 1, now(), now() + interval '48 hours')$$, 'one active listing per unique item');
SELECT pg_temp.expect_error($$INSERT INTO market.listings (id, seller_character_id, seller_account_id, seller_name, item_id, quantity, upgrade_level, price, fee, status, tax, version, created_at, expires_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'Li', 'jade', 1, 0, 0, 1, 'active', 0, 1, now(), now() + interval '1 hour')$$, 'price >= 1');
SELECT pg_temp.expect_error($$UPDATE market.listings SET price = 1, version = 2$$, 'price is fixed once listed');
SELECT pg_temp.expect_error($$UPDATE market.listings SET status = 'sold', buyer_account_id = seller_account_id, closed_at = now(), version = 2$$, 'no self-purchase');
UPDATE market.listings SET status = 'sold', buyer_account_id = '00000000-0000-7000-8000-000000000002', tax = 5, closed_at = now(), version = 2;
SELECT pg_temp.expect_error($$UPDATE market.listings SET status = 'active', closed_at = NULL, version = 3$$, 'closed listings are immutable');

-- crafting log ------------------------------------------------------------------------------------
INSERT INTO crafting.log (id, character_id, account_id, kind, subject, attempts, successes, chance_permille, seed, stones_spent, from_level, to_level, rules_version, occurred_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'upgrade', 'x', 1, 1, 900, 42, 120, 5, 6, 'crafting-1', now());
SELECT pg_temp.expect_error($$INSERT INTO crafting.log (id, character_id, account_id, kind, subject, attempts, successes, chance_permille, seed, stones_spent, from_level, to_level, rules_version, occurred_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'upgrade', 'x', 1, 0, 900, 42, 120, 5, 6, 'crafting-1', now())$$, 'a failed upgrade cannot raise the level');
SELECT pg_temp.expect_error($$INSERT INTO crafting.log (id, character_id, account_id, kind, subject, attempts, successes, chance_permille, seed, stones_spent, rules_version, occurred_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'craft', 'r', 3, 4, 850, 42, 15, 'crafting-1', now())$$, 'successes <= attempts');
SELECT pg_temp.expect_error($$UPDATE crafting.log SET successes = 0$$, 'crafting log append-only');

ROLLBACK;
\echo 'stage 6 integrity tests: PASSED'
