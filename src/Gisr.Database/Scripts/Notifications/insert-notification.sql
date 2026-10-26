INSERT INTO notification (id, recipient_account_id, kind, record_id, message, created_at, read_at)
VALUES (@Id, @RecipientAccountId, @Kind, @RecordId, @Message, now(), NULL);
