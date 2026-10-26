INSERT INTO schema_migration (script_id, checksum_sha256, applied_at)
VALUES (@ScriptId, @ChecksumSha256, now());
