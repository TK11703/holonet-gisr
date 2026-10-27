ALTER TABLE record
    ADD COLUMN deleted_at timestamptz NULL,
    ADD COLUMN deleted_by_account_id uuid NULL REFERENCES account (id),
    ADD CONSTRAINT ck_record_deleted_together CHECK ((deleted_at IS NULL) = (deleted_by_account_id IS NULL));

CREATE INDEX ix_record_deleted_at ON record (deleted_at) WHERE deleted_at IS NOT NULL;
