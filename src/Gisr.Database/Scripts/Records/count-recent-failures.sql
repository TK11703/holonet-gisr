SELECT count(*) AS failures,
       COALESCE(CEIL(EXTRACT(EPOCH FROM (min(attempted_at) + make_interval(secs => @WindowSeconds) - now()))), 0)::int AS retry_after_seconds
FROM record_override_attempt
WHERE record_id = @RecordId
  AND account_id = @AccountId
  AND NOT succeeded
  AND attempted_at > now() - make_interval(secs => @WindowSeconds);
