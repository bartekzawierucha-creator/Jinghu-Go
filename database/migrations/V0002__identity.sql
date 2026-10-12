-- Identity: accounts and opaque-token sessions (only SHA-256 hashes of tokens are stored).

CREATE SCHEMA identity;

CREATE TABLE identity.accounts (
    id                 uuid        PRIMARY KEY,
    email              text        NOT NULL CHECK (email = lower(btrim(email)) AND length(email) BETWEEN 3 AND 254 AND position('@' IN email) > 1),
    password_hash      text        NOT NULL CHECK (length(password_hash) > 0),
    created_at         timestamptz NOT NULL,
    failed_login_count integer     NOT NULL DEFAULT 0 CHECK (failed_login_count >= 0),
    locked_until       timestamptz NULL
);
CREATE UNIQUE INDEX accounts_email_uq ON identity.accounts (email);

CREATE TABLE identity.sessions (
    id                 uuid        PRIMARY KEY,
    account_id         uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    family_id          uuid        NOT NULL,
    access_token_hash  char(64)    NOT NULL CHECK (access_token_hash ~ '^[0-9A-F]{64}$'),
    access_expires_at  timestamptz NOT NULL,
    refresh_token_hash char(64)    NOT NULL CHECK (refresh_token_hash ~ '^[0-9A-F]{64}$'),
    refresh_expires_at timestamptz NOT NULL,
    created_at         timestamptz NOT NULL,
    rotated_at         timestamptz NULL,
    revoked_at         timestamptz NULL,
    revoke_reason      text        NULL CHECK (revoke_reason IN ('logout', 'refresh_token_reuse')),
    CHECK (access_expires_at > created_at),
    CHECK (refresh_expires_at > access_expires_at),
    CHECK ((revoked_at IS NULL) = (revoke_reason IS NULL))
);
CREATE UNIQUE INDEX sessions_access_hash_uq  ON identity.sessions (access_token_hash);
CREATE UNIQUE INDEX sessions_refresh_hash_uq ON identity.sessions (refresh_token_hash);
CREATE INDEX sessions_family_idx  ON identity.sessions (family_id);
CREATE INDEX sessions_account_idx ON identity.sessions (account_id);
-- Retention: sessions whose refresh token expired > 30 days ago may be purged.
CREATE INDEX sessions_refresh_expiry_idx ON identity.sessions (refresh_expires_at);

CREATE FUNCTION identity.sessions_immutable_tokens() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.access_token_hash <> OLD.access_token_hash OR NEW.refresh_token_hash <> OLD.refresh_token_hash
       OR NEW.account_id <> OLD.account_id OR NEW.family_id <> OLD.family_id THEN
        RAISE EXCEPTION 'session identity columns are immutable' USING ERRCODE = 'restrict_violation';
    END IF;
    IF OLD.revoked_at IS NOT NULL AND NEW.revoked_at IS DISTINCT FROM OLD.revoked_at THEN
        RAISE EXCEPTION 'a revoked session cannot be un-revoked' USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER sessions_immutable_tokens
    BEFORE UPDATE ON identity.sessions
    FOR EACH ROW EXECUTE FUNCTION identity.sessions_immutable_tokens();
