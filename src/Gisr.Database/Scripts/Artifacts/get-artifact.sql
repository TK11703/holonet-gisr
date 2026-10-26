SELECT a.id, a.record_id,
       CASE WHEN l.locked THEN '' ELSE a.file_name END AS file_name,
       CASE WHEN l.locked THEN '' ELSE a.content_type END AS content_type,
       CASE WHEN l.locked THEN 0 ELSE a.size_bytes END AS size_bytes,
       CASE WHEN l.locked THEN '' ELSE a.sha256 END AS sha256,
       CASE WHEN l.locked THEN '' ELSE a.blob_name END AS blob_name,
       CASE WHEN l.locked THEN NULL ELSE a.thumbnail_blob_name END AS thumbnail_blob_name,
       a.is_image,
       CASE WHEN l.locked THEN NULL ELSE a.description END AS description,
       a.uploaded_by_account_id,
       CASE WHEN l.locked THEN '' ELSE u.display_name END AS uploaded_by_display_name,
       a.uploaded_at, a.removed_at, a.removed_by_account_id, a.index_status, a.index_detail,
       l.locked AS is_locked
FROM artifact a
JOIN record r ON r.id = a.record_id
JOIN account u ON u.id = a.uploaded_by_account_id
CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, @Unlocked) AS locked) l
WHERE a.id = @Id
  AND a.removed_at IS NULL
  AND (@IsOfficer OR r.subject_account_id = @CallerAccountId);
