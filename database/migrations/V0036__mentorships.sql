-- 2.3: master and disciple (one master per disciple, up to 3 disciples per master).
CREATE TABLE social.mentorships (
    disciple_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    master_id    uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    since        timestamptz NOT NULL,
    CHECK (master_id <> disciple_id)
);
CREATE INDEX mentorships_master_idx ON social.mentorships (master_id);
