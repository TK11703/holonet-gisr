WITH updated AS (
    UPDATE artifact SET description = @Description
    WHERE id = @Id AND removed_at IS NULL
    RETURNING *
)
SELECT a.id, a.record_id, a.file_name, a.content_type, a.size_bytes, a.sha256, a.blob_name, a.thumbnail_blob_name, a.is_image,
       a.description, a.uploaded_by_account_id, u.display_name AS uploaded_by_display_name, a.uploaded_at,
       a.removed_at, a.removed_by_account_id, a.index_status, a.index_detail, false AS is_locked
FROM updated a
JOIN account u ON u.id = a.uploaded_by_account_id;
