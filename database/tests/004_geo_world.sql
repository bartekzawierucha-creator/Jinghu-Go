-- Stage 5 integrity tests: geolocation privacy constraints, world claims, and our haversine vs PostGIS.
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

-- The application's haversine (GeoMath.DistanceM, sphere R = 6371008.7714 m), transcribed to SQL, must agree with
-- PostGIS ST_Distance(geography, geography, use_spheroid => false) to within a centimetre on random point pairs.
CREATE FUNCTION pg_temp.haversine(lat1 float8, lon1 float8, lat2 float8, lon2 float8) RETURNS float8 LANGUAGE sql AS $$
    SELECT 2 * 6371008.7714 * asin(least(1, sqrt(
        power(sin(radians(lat2 - lat1) / 2), 2) + cos(radians(lat1)) * cos(radians(lat2)) * power(sin(radians(lon2 - lon1) / 2), 2))))
$$;
SELECT setseed(0.42);
SELECT pg_temp.expect(max(abs(
         pg_temp.haversine(lat1, lon1, lat2, lon2)
         - ST_Distance(ST_SetSRID(ST_MakePoint(lon1, lat1), 4326)::geography, ST_SetSRID(ST_MakePoint(lon2, lat2), 4326)::geography, false))) < 0.01,
       'haversine agrees with PostGIS sphere distance')
FROM (SELECT random() * 170 - 85 AS lat1, random() * 360 - 180 AS lon1, random() * 170 - 85 AS lat2, random() * 360 - 180 AS lon2
      FROM generate_series(1, 2000)) pts;
-- and short walking distances (the ones that decide rewards) agree to the millimetre
SELECT pg_temp.expect(max(abs(
         pg_temp.haversine(52.2297, 21.0122, 52.2297 + d, 21.0122 + d)
         - ST_Distance(ST_SetSRID(ST_MakePoint(21.0122, 52.2297), 4326)::geography, ST_SetSRID(ST_MakePoint(21.0122 + d, 52.2297 + d), 4326)::geography, false))) < 0.001,
       'short distances agree')
FROM (SELECT g / 100000.0 AS d FROM generate_series(1, 100) g) s;

INSERT INTO identity.accounts (id, email, password_hash, created_at) VALUES ('00000000-0000-7000-8000-000000000001', 'a@test.dev', 'h', now());
INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
VALUES ('00000000-0000-7000-8000-0000000000d1', '00000000-0000-7000-8000-000000000001', 2, 'finalized', 'r', 1, 'b', 'b', 'c',
        '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
        0, now(), now());
INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
SELECT '00000000-0000-7000-8000-0000000000c1', account_id, 'Li', 'LI', 'qi', 'r', 'b', 'b', 'c', stats, id, now() FROM characters.drafts;

-- geolocation: one active session; an ended session cannot keep a position
INSERT INTO geolocation.sessions (id, character_id, account_id, status, started_at, last_seen_at, rate_window_start, version, last_position, last_accuracy_m)
VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'active', now(), now(), now(), 1,
        ST_SetSRID(ST_MakePoint(21.0122, 52.2297), 4326)::geography, 8);
SELECT pg_temp.expect_error($$INSERT INTO geolocation.sessions (id, character_id, account_id, status, started_at, last_seen_at, rate_window_start, version)
    VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'active', now(), now(), now(), 0)$$, 'one active session per character');
SELECT pg_temp.expect_error($$UPDATE geolocation.sessions SET status = 'ended', ended_at = now()$$, 'ended session must forget its position');
UPDATE geolocation.sessions SET status = 'ended', ended_at = now(), last_position = NULL;
SELECT pg_temp.expect_error($$UPDATE geolocation.sessions SET last_accuracy_m = 0$$, 'accuracy > 0');
SELECT pg_temp.expect_error($$UPDATE geolocation.sessions SET distance_m = -1$$, 'distance >= 0');

-- world: one claim per node and character; well-formed keys
INSERT INTO world.node_claims VALUES ('82954.4202.10446.0', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'qi_spring', now());
SELECT pg_temp.expect_error($$INSERT INTO world.node_claims VALUES ('82954.4202.10446.0', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'qi_spring', now())$$, 'node claimed once per character');
SELECT pg_temp.expect_error($$INSERT INTO world.node_claims VALUES ('drop table', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'qi_spring', now())$$, 'node key format');
INSERT INTO world.flame_attunements VALUES ('00000000-0000-7000-8000-0000000000c1', 'wawel', now(), 1);
SELECT pg_temp.expect_error($$INSERT INTO world.flame_attunements VALUES ('00000000-0000-7000-8000-0000000000c1', 'wawel', now(), 1)$$, 'one attunement row per site');

ROLLBACK;
\echo 'stage 5 integrity tests: PASSED'
