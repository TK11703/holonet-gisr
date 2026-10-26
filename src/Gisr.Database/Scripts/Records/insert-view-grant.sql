INSERT INTO record_view_grant (id, record_id, account_id, token_hash, created_at, expires_at)
VALUES (@Id, @RecordId, @AccountId, @TokenHash, now(), now() + interval '15 minutes')
RETURNING expires_at;
