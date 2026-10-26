CREATE TABLE record_access_log (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    accessed_by_account_id uuid NOT NULL REFERENCES account (id),
    accessed_at timestamptz NOT NULL,
    kind text NOT NULL CHECK (kind IN ('record', 'artifact'))
);

CREATE INDEX ix_record_access_log_record ON record_access_log (record_id, accessed_at DESC);
