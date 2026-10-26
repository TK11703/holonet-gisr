WITH requeued AS (
    UPDATE artifact SET index_status = 'pending', index_detail = NULL
    WHERE index_status = 'skipped' AND index_detail = 'ocr_budget_exhausted' AND removed_at IS NULL
    RETURNING id, record_id)
INSERT INTO search_index_job (id, kind, record_id, artifact_id, status, attempts, available_at, created_at, updated_at)
SELECT gen_random_uuid(), 'artifact', record_id, id, 'pending', 0, now(), now(), now() FROM requeued;