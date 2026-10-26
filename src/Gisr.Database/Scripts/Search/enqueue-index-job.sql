INSERT INTO search_index_job (id, kind, record_id, artifact_id, status, attempts, available_at, created_at, updated_at)
SELECT @Id, @Kind, @RecordId, @ArtifactId::uuid, 'pending', 0, now(), now(), now()
-- A job that has not started yet will read the latest data anyway, so one pending job per target is enough.
WHERE NOT EXISTS (
    SELECT 1 FROM search_index_job
    WHERE status = 'pending' AND kind = @Kind AND record_id = @RecordId AND artifact_id IS NOT DISTINCT FROM @ArtifactId::uuid);