UPDATE record
SET deleted_at = now(),
    deleted_by_account_id = @DeletedByAccountId
WHERE id = @Id
  AND deleted_at IS NULL
  AND (@IsOfficer OR subject_account_id = @CallerAccountId);
