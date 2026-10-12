-- Representative data for the backup/restore drill (database/backup-restore-test.sh). Committed, not rolled back.
\set ON_ERROR_STOP 1
INSERT INTO identity.accounts (id, email, password_hash, created_at) VALUES
    ('00000000-0000-7000-8000-000000000001', 'a@test.dev', 'h', now()),
    ('00000000-0000-7000-8000-000000000002', 'b@test.dev', 'h', now());
INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000d1', '00000000-0000-7000-8000-000000000001', 2, 'finalized', 'r', 1, 'b', 'b', 'c',
        '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
        0, now(), now());
INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT '00000000-0000-7000-8000-0000000000c1', account_id, 'Li', 'LI', 'qi', 'r', 'b', 'b', 'c', stats, id, now() FROM characters.drafts;


INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 100, 100, 'initial_grant', 'c1', now()),
       (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', -30, 70, 'craft', 'k1', now());
INSERT INTO inventory.stack_entries (id, character_id, item_id, delta, quantity_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', 'iron_ore', 7, 7, 'gather', 'g1', now());
INSERT INTO inventory.items (id, character_id, account_id, item_id, upgrade_level, state, version, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'iron_sword', 3, 'equipped', 4, now());
INSERT INTO sects.sects (id, name, normalized_name, leader_character_id, leader_account_id, member_count, seat_lat, seat_lon, seat_set_at, version, created_at)
VALUES ('00000000-0000-7000-8000-00000000005a', 'Wudang', 'WUDANG', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 1, 32.4, 111.004, now(), 2, now());
INSERT INTO sects.members (character_id, sect_id, account_id, name, role, contribution, joined_at)
VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-00000000005a', '00000000-0000-7000-8000-000000000001', 'Li', 'leader', 250, now());
INSERT INTO sects.treasury_entries (id, sect_id, delta, balance_after, reason, reference_id, created_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000005a', 250, 250, 'donation', 'd1', now());
INSERT INTO territory.territories (key, owner_sect_id, claimed_at, version) VALUES ('2220.648', '00000000-0000-7000-8000-00000000005a', now(), 1);
INSERT INTO territory.ownership_changes (id, territory_key, to_sect_id, reason, changed_at) VALUES (gen_random_uuid(), '2220.648', '00000000-0000-7000-8000-00000000005a', 'claim', now());
INSERT INTO platform.audit_log (id, account_id, action, entity_type, entity_id, data, correlation_id, occurred_at)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 'drill.seed', 'drill', 'x', '{}', null, now());
