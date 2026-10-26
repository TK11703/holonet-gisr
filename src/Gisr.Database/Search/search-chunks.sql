WITH ranked AS (
    SELECT c.record_id, c.source_type, c.artifact_id, c.content,
           c.embedding <=> @Embedding AS distance,
           row_number() OVER (PARTITION BY c.record_id, c.artifact_id ORDER BY c.embedding <=> @Embedding) AS rank_in_source
    FROM search_chunk c
    JOIN record r ON r.id = c.record_id
    LEFT JOIN artifact a ON a.id = c.artifact_id
    WHERE (c.artifact_id IS NULL OR a.removed_at IS NULL)
      -- Access scoping comes before ranking so restricted data is never returned or counted.
      AND ((@AllRecords AND @IsOfficer)
           OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId AND NOT record_is_locked(r.is_sealed, @IsOfficer, false)))
      AND (@Affiliation::text IS NULL OR r.affiliation = @Affiliation)
      AND c.embedding <=> @Embedding < @MaxDistance)
SELECT k.record_id, r.title AS record_title, r.affiliation, s.display_name AS subject_display_name, r.is_sealed,
       k.source_type, k.artifact_id, a.file_name AS artifact_file_name, k.content, k.distance
FROM ranked k
JOIN record r ON r.id = k.record_id
JOIN account s ON s.id = r.subject_account_id
LEFT JOIN artifact a ON a.id = k.artifact_id
WHERE k.rank_in_source = 1
ORDER BY k.distance, k.record_id, k.artifact_id
LIMIT @PageSize OFFSET @Offset;