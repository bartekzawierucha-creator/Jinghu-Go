-- 1.9: Destiny Tokens are earned (check-in streaks, season pass, world bosses) and spent on an extra Meditation on the Dao.

ALTER TABLE characters.destiny_token_entries DROP CONSTRAINT destiny_token_entries_reason_check;
ALTER TABLE characters.destiny_token_entries ADD CONSTRAINT destiny_token_entries_reason_check
    CHECK (reason IN ('initial_grant', 'reroll', 'streak', 'season', 'boss', 'dao_meditation'));
