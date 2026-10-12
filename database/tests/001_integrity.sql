-- Database-level integrity tests. Run inside one transaction and rolled back; any failure aborts
-- with a non-zero exit code (psql ON_ERROR_STOP). Invoked by database/test.sh.
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

SELECT pg_temp.expect(postgis_full_version() IS NOT NULL, 'postgis available');

-- identity -----------------------------------------------------------------------------------
INSERT INTO identity.accounts (id, email, password_hash, created_at)
VALUES ('00000000-0000-7000-8000-000000000001', 'a@test.dev', 'hash', now()),
       ('00000000-0000-7000-8000-000000000002', 'b@test.dev', 'hash', now());

SELECT pg_temp.expect_error($$INSERT INTO identity.accounts (id, email, password_hash, created_at)
    VALUES (gen_random_uuid(), 'A@test.dev', 'h', now())$$, 'email must be normalised');
SELECT pg_temp.expect_error($$INSERT INTO identity.accounts (id, email, password_hash, created_at)
    VALUES (gen_random_uuid(), 'a@test.dev', 'h', now())$$, 'email unique');
SELECT pg_temp.expect_error($$UPDATE identity.accounts SET failed_login_count = -1$$, 'failed count >= 0');

INSERT INTO identity.sessions (id, account_id, family_id, access_token_hash, access_expires_at, refresh_token_hash, refresh_expires_at, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', gen_random_uuid(), repeat('A', 64), now() + interval '15 min', repeat('B', 64), now() + interval '30 days', now());
SELECT pg_temp.expect_error($$UPDATE identity.sessions SET access_token_hash = repeat('C', 64)$$, 'token hashes immutable');
SELECT pg_temp.expect_error($$UPDATE identity.sessions SET revoked_at = now()$$, 'revoke requires reason');
UPDATE identity.sessions SET revoked_at = now(), revoke_reason = 'logout';
SELECT pg_temp.expect_error($$UPDATE identity.sessions SET revoked_at = NULL, revoke_reason = NULL$$, 'cannot un-revoke');
SELECT pg_temp.expect_error($$INSERT INTO identity.sessions (id, account_id, family_id, access_token_hash, access_expires_at, refresh_token_hash, refresh_expires_at, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000002', gen_random_uuid(), repeat('A', 64), now() + interval '1 min', repeat('D', 64), now() + interval '1 day', now())$$,
    'access hash unique');

-- destiny token ledger -------------------------------------------------------------------------
INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 10, 10, 'initial_grant', 'acc1', now());
INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', -3, 7, 'reroll', 'draft:1', now());
SELECT pg_temp.expect((SELECT balance FROM characters.destiny_token_balances WHERE account_id = '00000000-0000-7000-8000-000000000001') = 7, 'balance follows ledger');

SELECT pg_temp.expect_error($$INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', -1, 100, 'reroll', 'draft:2', now())$$, 'wrong balance_after rejected');
SELECT pg_temp.expect_error($$INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', -8, -1, 'reroll', 'draft:3', now())$$, 'negative balance rejected');
SELECT pg_temp.expect_error($$INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', -1, 6, 'reroll', 'draft:1', now())$$, 'duplicate (account, reason, reference) rejected');
SELECT pg_temp.expect_error($$INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 0, 7, 'reroll', 'draft:4', now())$$, 'zero delta rejected');
SELECT pg_temp.expect_error($$UPDATE characters.destiny_token_entries SET delta = 100$$, 'ledger entries immutable');
SELECT pg_temp.expect_error($$DELETE FROM characters.destiny_token_entries$$, 'ledger entries undeletable');
SELECT pg_temp.expect_error($$UPDATE characters.destiny_token_balances SET balance = 999$$, 'balance only via ledger');
SELECT pg_temp.expect(NOT EXISTS (SELECT 1 FROM characters.destiny_ledger_mismatches), 'reconciliation view empty');

-- drafts & characters ----------------------------------------------------------------------------
INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000d1', '00000000-0000-7000-8000-000000000001', 1, 'active', 'char-creation-1', 18446744073709551615,
        'bg_farmer_son', 'bl_mortal', 'co_ordinary',
        '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
        0, now(), now());
SELECT pg_temp.expect_error($$INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
    SELECT gen_random_uuid(), account_id, 1, 'active', rules_version, 1, background_trait, bloodline_trait, constitution_trait, stats, 0, now(), now() FROM characters.drafts$$,
    'one active draft per account');
SELECT pg_temp.expect_error($$UPDATE characters.drafts SET stats = '[{"id":"strength","value":10}]'$$, 'stats must have all nine');
SELECT pg_temp.expect_error($$UPDATE characters.drafts SET stats = jsonb_set(stats, '{0,value}', '"ten"')$$, 'stats must be numbers');
SELECT pg_temp.expect_error($$UPDATE characters.drafts SET last_seed = 18446744073709551616$$, 'seed fits u64');

INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT gen_random_uuid(), account_id, 'Łucja', 'ŁUCJA', 'qi', rules_version, background_trait, bloodline_trait, constitution_trait, stats, id, now() FROM characters.drafts;
SELECT pg_temp.expect_error($$INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
    SELECT gen_random_uuid(), '00000000-0000-7000-8000-000000000002', 'łucja', 'ŁUCJA', 'qi', rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, now() FROM characters.characters$$,
    'character names unique');
SELECT pg_temp.expect_error($$UPDATE characters.characters SET path = 'darkness'$$, 'path enum');
SELECT pg_temp.expect_error($$DELETE FROM identity.accounts WHERE id = '00000000-0000-7000-8000-000000000001'$$, 'account with characters cannot be hard-deleted');

-- platform -----------------------------------------------------------------------------------------
INSERT INTO platform.idempotency_records VALUES ('00000000-0000-7000-8000-000000000001', 'op', 'key-12345', repeat('F', 64), '{}', now());
SELECT pg_temp.expect_error($$INSERT INTO platform.idempotency_records VALUES ('00000000-0000-7000-8000-000000000001', 'op', 'key-12345', repeat('F', 64), '{}', now())$$, 'idempotency key unique');
SELECT pg_temp.expect_error($$INSERT INTO platform.idempotency_records VALUES ('00000000-0000-7000-8000-000000000001', 'op', 'bad key!', repeat('F', 64), '{}', now())$$, 'idempotency key format');

INSERT INTO platform.audit_log (id, action, entity_type, entity_id, occurred_at) VALUES (gen_random_uuid(), 'test', 'x', '1', now());
SELECT pg_temp.expect_error($$UPDATE platform.audit_log SET action = 'tampered'$$, 'audit immutable');
SELECT pg_temp.expect_error($$DELETE FROM platform.audit_log$$, 'audit undeletable');

ROLLBACK;
\echo 'database integrity tests: PASSED'
