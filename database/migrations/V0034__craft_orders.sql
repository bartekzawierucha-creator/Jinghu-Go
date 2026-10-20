-- 2.3: craft orders — an escrowed reward for delivering stackable items.
CREATE TABLE market.craft_orders (
    id            uuid        PRIMARY KEY,
    orderer_id    uuid        NOT NULL REFERENCES characters.characters (id) ON DELETE CASCADE,
    orderer_name  text        NOT NULL CHECK (char_length(orderer_name) BETWEEN 1 AND 64),
    item_id       text        NOT NULL CHECK (item_id ~ '^[a-z0-9_]{1,80}$'),
    quantity      bigint      NOT NULL CHECK (quantity > 0),
    reward        bigint      NOT NULL CHECK (reward > 0),
    created_at    timestamptz NOT NULL,
    expires_at    timestamptz NOT NULL,
    status        text        NOT NULL CHECK (status IN ('open', 'fulfilled', 'cancelled')),
    fulfilled_by  uuid        NULL REFERENCES characters.characters (id) ON DELETE SET NULL,
    closed_at     timestamptz NULL,
    CHECK ((status = 'open') = (closed_at IS NULL))
);
CREATE INDEX craft_orders_open_idx ON market.craft_orders (id DESC) WHERE status = 'open';
CREATE INDEX craft_orders_orderer_idx ON market.craft_orders (orderer_id) WHERE status = 'open';
