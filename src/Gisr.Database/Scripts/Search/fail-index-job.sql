UPDATE search_index_job
SET status = CASE WHEN @Attempts >= @MaxAttempts THEN 'failed' ELSE 'pending' END,
    available_at = now() + make_interval(secs => @BackoffSeconds),
    last_error = @Error,
    updated_at = now()
WHERE id = @Id;