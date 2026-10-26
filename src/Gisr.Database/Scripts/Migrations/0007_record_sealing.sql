ALTER TABLE record
    ADD COLUMN is_sealed boolean NOT NULL DEFAULT false,
    ADD COLUMN override_code_hash text NULL,
    ADD CONSTRAINT ck_record_sealed_is_service CHECK (NOT is_sealed OR author_account_id <> subject_account_id),
    ADD CONSTRAINT ck_record_sealed_has_code CHECK (is_sealed = (override_code_hash IS NOT NULL));

-- The one shared "locked for caller" predicate; every query that returns record content goes through it.
CREATE FUNCTION record_is_locked(is_sealed boolean, is_officer boolean, is_unlocked boolean)
RETURNS boolean
LANGUAGE sql IMMUTABLE PARALLEL SAFE
RETURN is_sealed AND NOT (is_officer OR is_unlocked);

CREATE TABLE record_view_grant (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    account_id uuid NOT NULL REFERENCES account (id),
    token_hash bytea NOT NULL UNIQUE,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL
);

CREATE INDEX ix_record_view_grant_record ON record_view_grant (record_id);

CREATE TABLE record_override_attempt (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    account_id uuid NOT NULL REFERENCES account (id),
    attempted_at timestamptz NOT NULL,
    succeeded boolean NOT NULL
);

CREATE INDEX ix_record_override_attempt ON record_override_attempt (record_id, account_id, attempted_at DESC);
