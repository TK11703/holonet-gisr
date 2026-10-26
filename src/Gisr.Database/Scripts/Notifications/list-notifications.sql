SELECT id, recipient_account_id, kind, record_id, message, created_at, read_at
FROM notification
WHERE recipient_account_id = @RecipientAccountId
  AND (NOT @UnreadOnly OR read_at IS NULL)
ORDER BY created_at DESC, id DESC
LIMIT @PageSize;
