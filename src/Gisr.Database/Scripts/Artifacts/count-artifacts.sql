SELECT count(*)
FROM artifact a
JOIN record r ON r.id = a.record_id
CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, @Unlocked) AS locked) l
WHERE a.record_id = @RecordId
  AND r.deleted_at IS NULL
  AND a.removed_at IS NULL
  AND NOT l.locked
  AND (@ImagesOnly = false OR a.is_image)
  AND (@IsOfficer OR r.subject_account_id = @CallerAccountId);
