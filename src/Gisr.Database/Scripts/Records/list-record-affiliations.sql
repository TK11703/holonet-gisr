WITH visible AS (
    SELECT r.affiliation_primary, r.affiliation_secondary
    FROM record r
    CROSS JOIN LATERAL (SELECT record_is_locked(r.is_sealed, @IsOfficer, false) AS locked) l
    WHERE r.deleted_at IS NULL
      AND NOT l.locked
      AND (
            (@AllRecords AND @IsOfficer AND (@SubjectAccountId::uuid IS NULL OR r.subject_account_id = @SubjectAccountId))
            OR (NOT @AllRecords AND r.subject_account_id = @CallerAccountId))
)
SELECT 'primary' AS kind, affiliation_primary AS value
FROM visible
WHERE affiliation_primary <> ''
GROUP BY affiliation_primary
UNION ALL
SELECT 'secondary' AS kind, affiliation_secondary AS value
FROM visible
WHERE affiliation_secondary IS NOT NULL
  AND (@PrimaryAffiliation::text IS NULL OR affiliation_primary = @PrimaryAffiliation)
GROUP BY affiliation_secondary
ORDER BY kind, value;
