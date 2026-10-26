SELECT r.id, r.subject_account_id, r.author_account_id,
       CASE WHEN l.locked THEN '' ELSE r.title END AS title,
       CASE WHEN l.locked THEN '' ELSE r.affiliation END AS affiliation,
       CASE WHEN l.locked THEN NULL ELSE r.engagement_start END AS engagement_start,
       CASE WHEN l.locked THEN NULL ELSE r.engagement_end END AS engagement_end,
       CASE WHEN l.locked THEN '{}' ELSE r.engagement_detail::text END AS engagement_detail,
       r.is_sealed, l.locked AS is_locked,
       CASE WHEN l.locked THEN 0 ELSE (SELECT count(*)::int FROM artifact x WHERE x.record_id = r.id AND x.removed_at IS NULL) END AS artifact_count,
       r.version, r.created_at, r.updated_at, r.updated_by_account_id,
       CASE WHEN l.locked THEN '' ELSE s.display_name END AS subject_display_name,
       CASE WHEN l.locked THEN '' ELSE a.display_name END AS author_display_name
FROM record r
CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, false) AS locked) l
JOIN account s ON s.id = r.subject_account_id
JOIN account a ON a.id = r.author_account_id
WHERE (
        (@AllRecords AND @IsOfficer AND (@SubjectAccountId::uuid IS NULL OR r.subject_account_id = @SubjectAccountId))
        OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId))
  AND (@Affiliation::text IS NULL OR (NOT l.locked AND r.affiliation = @Affiliation))
ORDER BY
    CASE WHEN @Sort = 'titleAsc' AND NOT l.locked THEN r.title END ASC NULLS LAST,
    CASE WHEN @Sort = 'titleDesc' AND NOT l.locked THEN r.title END DESC NULLS LAST,
    CASE WHEN @Sort = 'updatedAsc' THEN r.updated_at END ASC,
    CASE WHEN @Sort = 'updatedDesc' THEN r.updated_at END DESC,
    r.id
LIMIT @PageSize OFFSET @Offset;
