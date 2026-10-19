-- 2.2: spirit pet evolution stages (0 young … 3 mythic).
ALTER TABLE combat.spirit_pets ADD COLUMN stage integer NOT NULL DEFAULT 0 CHECK (stage BETWEEN 0 AND 3);
