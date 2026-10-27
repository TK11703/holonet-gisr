UPDATE record
SET is_sealed = false,
    override_code_hash = NULL,
    version = version + 1,
    updated_at = now(),
    updated_by_account_id = @UpdatedByAccountId
WHERE id = @Id
  AND subject_account_id = @SubjectAccountId
  AND is_sealed
  AND deleted_at IS NULL;
