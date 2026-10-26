-- A deferral is not a failure: the attempt is handed back so an outage can never use up the job's retries.
UPDATE search_index_job
SET status = 'pending',
    attempts = GREATEST(attempts - 1, 0),
    available_at = now() + make_interval(secs => @DelaySeconds),
    last_error = @Error,
    updated_at = now()
WHERE id = @Id;
