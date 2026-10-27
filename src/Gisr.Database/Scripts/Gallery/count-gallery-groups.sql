SELECT count(*)
FROM record r
WHERE NOT r.is_sealed
  AND r.deleted_at IS NULL
  AND EXISTS (SELECT 1 FROM artifact a WHERE a.record_id = r.id AND a.is_image AND a.removed_at IS NULL)
  AND ((@AllRecords AND @IsOfficer) OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId));
