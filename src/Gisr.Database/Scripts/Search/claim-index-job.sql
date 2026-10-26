UPDATE search_index_job
SET status = 'running', attempts = attempts + 1, updated_at = now()
WHERE id = (
    SELECT id FROM search_index_job
    WHERE (status = 'pending' AND available_at <= now())
       -- A worker that died mid-job leaves it running; pick it up again after a grace period.
       OR (status = 'running' AND updated_at < now() - make_interval(secs => @StaleSeconds))
    ORDER BY available_at, created_at
    LIMIT 1
    FOR UPDATE SKIP LOCKED)
RETURNING id, kind, record_id, artifact_id, attempts;