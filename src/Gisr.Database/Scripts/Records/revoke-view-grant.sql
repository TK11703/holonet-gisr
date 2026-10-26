UPDATE record_view_grant
SET revoked_at = COALESCE(revoked_at, now())
WHERE record_id = @RecordId AND account_id = @AccountId AND token_hash = @TokenHash;
