-- Records store "Primary" or "Primary › Secondary"; these split that text so the list can filter and facet on each part.
ALTER TABLE record
    ADD COLUMN affiliation_primary text GENERATED ALWAYS AS (split_part(affiliation, ' ' || chr(8250) || ' ', 1)) STORED,
    ADD COLUMN affiliation_secondary text GENERATED ALWAYS AS (NULLIF(split_part(affiliation, ' ' || chr(8250) || ' ', 2), '')) STORED;

CREATE INDEX ix_record_affiliation_parts ON record (affiliation_primary, affiliation_secondary) WHERE deleted_at IS NULL;
