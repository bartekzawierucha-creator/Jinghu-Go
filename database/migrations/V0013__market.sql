-- Market: whole-lot listings with escrow. Goods leave the seller's ledger (stack) or are flagged 'listed' (unique item)
-- when listed; money and goods move in the purchase transaction. The version column serialises competing buyers.

CREATE SCHEMA market;

CREATE TABLE market.listings (
    id                  uuid        PRIMARY KEY,
    seller_character_id uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    seller_account_id   uuid        NOT NULL REFERENCES identity.accounts (id) ON DELETE CASCADE,
    seller_name         text        NOT NULL CHECK (length(seller_name) BETWEEN 1 AND 64),
    item_id             text        NOT NULL CHECK (item_id ~ '^[a-z_]{1,64}$'),
    quantity            bigint      NOT NULL CHECK (quantity >= 1),
    instance_id         uuid        NULL REFERENCES inventory.items (id) ON DELETE RESTRICT,
    upgrade_level       integer     NOT NULL CHECK (upgrade_level >= 0),
    price               bigint      NOT NULL CHECK (price >= 1),
    fee                 bigint      NOT NULL CHECK (fee >= 0),
    status              text        NOT NULL CHECK (status IN ('active', 'sold', 'cancelled')),
    buyer_character_id  uuid        NULL REFERENCES characters.characters (id) ON DELETE SET NULL,
    buyer_account_id    uuid        NULL REFERENCES identity.accounts (id) ON DELETE SET NULL,
    tax                 bigint      NOT NULL CHECK (tax >= 0 AND tax < price),
    version             integer     NOT NULL CHECK (version >= 1),
    created_at          timestamptz NOT NULL,
    expires_at          timestamptz NOT NULL CHECK (expires_at > created_at),
    closed_at           timestamptz NULL,
    CHECK (instance_id IS NULL OR quantity = 1),
    CHECK ((status = 'active') = (closed_at IS NULL)),
    CHECK (status <> 'sold' OR buyer_account_id IS DISTINCT FROM seller_account_id)
);
CREATE INDEX listings_browse_idx ON market.listings (item_id, id DESC) WHERE status = 'active';
CREATE INDEX listings_active_idx ON market.listings (id DESC) WHERE status = 'active';
CREATE INDEX listings_seller_idx ON market.listings (seller_character_id, id DESC);
-- A unique item can be in at most one active listing.
CREATE UNIQUE INDEX listings_one_active_per_instance ON market.listings (instance_id) WHERE status = 'active' AND instance_id IS NOT NULL;

-- Closed listings are history: once sold or cancelled, a row never changes again.
CREATE FUNCTION market.listings_closed_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.status <> 'active' THEN
        RAISE EXCEPTION 'listing % is closed', OLD.id USING ERRCODE = 'restrict_violation';
    END IF;
    IF NEW.version <> OLD.version + 1 OR NEW.price <> OLD.price OR NEW.quantity <> OLD.quantity OR NEW.item_id <> OLD.item_id
       OR NEW.seller_character_id <> OLD.seller_character_id OR NEW.instance_id IS DISTINCT FROM OLD.instance_id THEN
        RAISE EXCEPTION 'illegal listing update' USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER listings_closed_immutable BEFORE UPDATE ON market.listings
    FOR EACH ROW EXECUTE FUNCTION market.listings_closed_immutable();
CREATE TRIGGER listings_no_delete BEFORE DELETE ON market.listings
    FOR EACH ROW WHEN (OLD.status = 'active') EXECUTE FUNCTION platform.forbid_modification();
