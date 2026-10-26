SELECT count(*) FROM notification WHERE recipient_account_id = @RecipientAccountId AND read_at IS NULL;
