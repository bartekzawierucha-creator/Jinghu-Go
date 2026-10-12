-- Geolocation: exploration sessions. Only the LAST accepted position is kept (PostGIS geography),
-- and it is set to NULL when the session ends. No sample history is stored (data minimisation).

CREATE SCHEMA geolocation;

CREATE TABLE geolocation.sessions (
    id                 uuid        PRIMARY KEY,
    character_id       uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    account_id         uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    status             text        NOT NULL CHECK (status IN ('active', 'ended')),
    started_at         timestamptz NOT NULL,
    last_seen_at       timestamptz NOT NULL,
    ended_at           timestamptz NULL,
    last_position      geography(Point, 4326) NULL,
    last_accuracy_m    double precision NULL CHECK (last_accuracy_m IS NULL OR last_accuracy_m > 0),
    last_captured_at   timestamptz NULL,
    last_received_at   timestamptz NULL,
    last_speed_mps     double precision NOT NULL DEFAULT 0 CHECK (last_speed_mps >= 0),
    relocation_until   timestamptz NULL,
    distance_m         double precision NOT NULL DEFAULT 0 CHECK (distance_m >= 0),
    accepted_samples   integer NOT NULL DEFAULT 0 CHECK (accepted_samples >= 0),
    dropped_samples    integer NOT NULL DEFAULT 0 CHECK (dropped_samples >= 0),
    anomalies          integer NOT NULL DEFAULT 0 CHECK (anomalies >= 0),
    flagged            boolean NOT NULL DEFAULT false,
    rate_window_start  timestamptz NOT NULL,
    rate_window_count  integer NOT NULL DEFAULT 0 CHECK (rate_window_count >= 0),
    version            integer NOT NULL CHECK (version >= 0),
    CHECK ((status = 'ended') = (ended_at IS NOT NULL)),
    CHECK (status = 'active' OR last_position IS NULL)          -- ended sessions forget where the player was
);
CREATE UNIQUE INDEX sessions_one_active_uq ON geolocation.sessions (character_id) WHERE status = 'active';
-- Spatial index for future anti-abuse / territory queries over live positions.
CREATE INDEX sessions_position_gix ON geolocation.sessions USING gist (last_position) WHERE status = 'active';
