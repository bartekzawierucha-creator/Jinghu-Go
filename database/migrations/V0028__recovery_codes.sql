-- 2.0: one-time recovery codes for password reset without e-mail delivery (only a PBKDF2 hash is stored).
CREATE TABLE identity.recovery_codes (
    account_id  uuid        PRIMARY KEY REFERENCES identity.accounts (id) ON DELETE CASCADE,
    code_hash   text        NOT NULL CHECK (length(code_hash) > 0),
    created_at  timestamptz NOT NULL
);
