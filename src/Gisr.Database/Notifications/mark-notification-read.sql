UPDATE notification
SET read_at = COALESCE(read_at, now())
WHERE id = @Id AND recipient_account_id = @RecipientAccountId;
