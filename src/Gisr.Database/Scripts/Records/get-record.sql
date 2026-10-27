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
CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, @Unlocked) AS locked) l
JOIN account s ON s.id = r.subject_account_id
JOIN account a ON a.id = r.author_account_id
WHERE r.id = @Id AND r.deleted_at IS NULL AND (@IsOfficer OR r.subject_account_id = @CallerAccountId);
