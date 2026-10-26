SELECT override_code_hash
FROM record
WHERE id = @Id AND subject_account_id = @SubjectAccountId AND is_sealed;
