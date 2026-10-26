SELECT id, record_id, accessed_by_account_id, accessed_at, kind
FROM record_access_log
WHERE record_id = @RecordId
ORDER BY accessed_at DESC, id DESC;
