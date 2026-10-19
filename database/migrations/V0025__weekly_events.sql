-- 1.9: weekly world events ('W<iso-year>-<week>') and community challenges summed over all cultivators.

ALTER TABLE quests.progress DROP CONSTRAINT progress_period_check;
ALTER TABLE quests.progress ADD CONSTRAINT progress_period_check CHECK (period ~ '^(\d{4}-\d{2}-\d{2}|life|S\d{4}|W\d{4}-\d{2})$');

-- Community totals: SUM(progress) for one week and metric.
CREATE INDEX progress_period_key_idx ON quests.progress (period, quest_id);
