SELECT count(*) FROM (
    SELECT DISTINCT c.record_id, c.artifact_id
    FROM search_chunk c
    JOIN record r ON r.id = c.record_id
    LEFT JOIN artifact a ON a.id = c.artifact_id
    WHERE (c.artifact_id IS NULL OR a.removed_at IS NULL)
      AND ((@AllRecords AND @IsOfficer)
           OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId AND NOT record_is_locked(r.is_sealed, @IsOfficer, false)))
      AND (@Affiliation::text IS NULL OR r.affiliation = @Affiliation)
      AND c.embedding <=> @Embedding < @MaxDistance) hits;