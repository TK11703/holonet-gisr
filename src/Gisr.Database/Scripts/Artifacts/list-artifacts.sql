SELECT a.id, a.record_id, a.file_name, a.content_type, a.size_bytes, a.sha256, a.blob_name, a.thumbnail_blob_name, a.is_image,
       a.description, a.uploaded_by_account_id, u.display_name AS uploaded_by_display_name, a.uploaded_at,
       a.removed_at, a.removed_by_account_id, a.index_status, a.index_detail, false AS is_locked
FROM artifact a
JOIN record r ON r.id = a.record_id
JOIN account u ON u.id = a.uploaded_by_account_id
CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, @Unlocked) AS locked) l
WHERE a.record_id = @RecordId
  AND r.deleted_at IS NULL
  AND a.removed_at IS NULL
  AND NOT l.locked
  AND (@ImagesOnly = false OR a.is_image)
  AND (@IsOfficer OR r.subject_account_id = @CallerAccountId)
ORDER BY a.uploaded_at DESC, a.id DESC
LIMIT @PageSize OFFSET @Offset;
