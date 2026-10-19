-- 2.0: blocking and player reports. Reports keep only the reported excerpt (no location); kept 180 days.
CREATE TABLE social.blocks (
    blocker_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    blocked_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL,
    PRIMARY KEY (blocker_id, blocked_id),
    CHECK (blocker_id <> blocked_id)
);

CREATE TABLE social.reports (
    id           uuid        PRIMARY KEY,
    reporter_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    reported_id  uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    reason       text        NOT NULL CHECK (reason IN ('offensive_name', 'harassment', 'spam', 'cheating', 'other')),
    excerpt      text        NULL CHECK (excerpt IS NULL OR (char_length(excerpt) <= 300 AND excerpt !~ '[[:cntrl:]]')),
    created_at   timestamptz NOT NULL,
    reviewed_at  timestamptz NULL,
    decision     text        NULL
);
CREATE INDEX reports_reporter_idx ON social.reports (reporter_id, created_at);
CREATE INDEX reports_open_idx ON social.reports (created_at) WHERE reviewed_at IS NULL;
