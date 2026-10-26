CREATE TABLE IF NOT EXISTS schema_migration (
    script_id text PRIMARY KEY,
    checksum_sha256 text NOT NULL,
    applied_at timestamptz NOT NULL
);
