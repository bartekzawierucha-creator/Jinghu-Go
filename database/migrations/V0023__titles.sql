-- MVP 1.5: titles shown above the character, secret achievements, seasonal events (period 'S<year>'), story chapters.

ALTER TABLE quests.progress DROP CONSTRAINT progress_period_check;
ALTER TABLE quests.progress ADD CONSTRAINT progress_period_check CHECK (period ~ '^(\d{4}-\d{2}-\d{2}|life|S\d{4})$');

-- The one title a character shows above its head (any title it has earned; checked when read).
CREATE TABLE quests.selected_titles (
    character_id  uuid        PRIMARY KEY REFERENCES characters.characters (id) ON DELETE CASCADE,
    title_id      text        NOT NULL CHECK (title_id ~ '^[a-z0-9_]{1,80}$'),
    updated_at    timestamptz NOT NULL
);
