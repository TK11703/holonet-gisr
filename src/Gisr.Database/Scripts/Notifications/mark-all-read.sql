UPDATE notification
SET read_at = now()
WHERE recipient_account_id = @RecipientAccountId AND read_at IS NULL;
