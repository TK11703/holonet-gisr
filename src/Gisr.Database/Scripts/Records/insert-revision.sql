INSERT INTO record_revision (id, record_id, version, changed_by_account_id, changed_at, snapshot)
SELECT @Id, r.id, r.version, @ChangedByAccountId, now(),
       jsonb_build_object(
           'title', r.title,
           'affiliation', r.affiliation,
           'engagementStart', r.engagement_start,
           'engagementEnd', r.engagement_end,
           'engagementDetail', r.engagement_detail,
           'version', r.version,
           'subjectAccountId', r.subject_account_id,
           'authorAccountId', r.author_account_id,
           'isSealed', r.is_sealed,
           'hasOverrideCode', (r.override_code_hash IS NOT NULL),
           'incidents', COALESCE((
               SELECT jsonb_agg(
                          jsonb_build_object('occurredOn', i.occurred_on, 'title', i.title, 'description', i.description)
                          ORDER BY i.sort_order)
               FROM record_incident i
               WHERE i.record_id = r.id), '[]'::jsonb),
           'awards', COALESCE((
               SELECT jsonb_agg(
                          jsonb_build_object('name', a.name, 'awardedOn', a.awarded_on, 'issuer', a.issuer, 'description', a.description)
                          ORDER BY a.sort_order)
               FROM record_award a
               WHERE a.record_id = r.id), '[]'::jsonb))
FROM record r
WHERE r.id = @RecordId;
