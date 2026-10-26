SELECT count(*)
FROM record r
CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, false) AS locked) l
WHERE (
        (@AllRecords AND @IsOfficer AND (@SubjectAccountId::uuid IS NULL OR r.subject_account_id = @SubjectAccountId))
        OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId))
  AND (@Affiliation::text IS NULL OR (NOT l.locked AND r.affiliation = @Affiliation));
