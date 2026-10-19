-- 1.9: price history (sales of an item in the last days) and search by unit price.
CREATE INDEX listings_sales_idx ON market.listings (item_id, closed_at DESC) WHERE status = 'sold';
