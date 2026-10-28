SELECT r.id AS record_id, r.title, r.affiliation, s.display_name AS subject_display_name,
       g.image_count, g.cover_artifact_ids
FROM record r
JOIN account s ON s.id = r.subject_account_id
CROSS JOIN LATERAL (
    SELECT count(*)::int AS image_count,
           (array_agg(a.id ORDER BY a.uploaded_at DESC, a.id DESC))[1:4] AS cover_artifact_ids,
           max(a.uploaded_at) AS latest_upload
    FROM artifact a
    WHERE a.record_id = r.id AND a.is_image AND a.removed_at IS NULL
) g
-- Sealed records are excluded for everyone; their images are reachable only from the record page.
WHERE NOT r.is_sealed
  AND r.deleted_at IS NULL
  AND g.image_count > 0
  AND ((@AllRecords AND @IsOfficer) OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId))
ORDER BY
    CASE WHEN @Sort = 'titleAsc' THEN r.title END ASC,
    CASE WHEN @Sort = 'titleDesc' THEN r.title END DESC,
    CASE WHEN @Sort = 'updatedDesc' THEN g.latest_upload END DESC,
    g.latest_upload DESC, r.id
LIMIT @PageSize OFFSET @Offset;
