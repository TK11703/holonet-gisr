UPDATE artifact
SET removed_at = now(), removed_by_account_id = @RemovedByAccountId
WHERE id = @Id AND removed_at IS NULL;
