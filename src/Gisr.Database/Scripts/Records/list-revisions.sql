SELECT v.id, v.record_id, v.version, v.changed_by_account_id, c.display_name AS changed_by_display_name,
       v.changed_at, v.snapshot->>'title' AS title, v.snapshot->>'affiliation' AS affiliation,
       v.snapshot::text AS snapshot
FROM record_revision v
JOIN account c ON c.id = v.changed_by_account_id
WHERE v.record_id = @RecordId
ORDER BY v.version DESC
LIMIT @PageSize OFFSET @Offset;
