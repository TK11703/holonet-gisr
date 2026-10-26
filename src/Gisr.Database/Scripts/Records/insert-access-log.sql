INSERT INTO record_access_log (id, record_id, accessed_by_account_id, accessed_at, kind)
VALUES (@Id, @RecordId, @AccessedByAccountId, now(), @Kind);
