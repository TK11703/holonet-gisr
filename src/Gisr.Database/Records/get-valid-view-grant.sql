SELECT EXISTS (
    SELECT 1
    FROM record_view_grant
    WHERE record_id = @RecordId
      AND account_id = @AccountId
      AND token_hash = @TokenHash
      AND revoked_at IS NULL
      AND expires_at > now());
