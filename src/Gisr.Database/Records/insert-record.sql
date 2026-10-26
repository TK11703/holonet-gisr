INSERT INTO record (id, subject_account_id, author_account_id, title, affiliation, engagement_start, engagement_end,
                    engagement_detail, version, created_at, updated_at, updated_by_account_id, is_sealed, override_code_hash)
VALUES (@Id, @SubjectAccountId, @AuthorAccountId, @Title, @Affiliation, @EngagementStart::date, @EngagementEnd::date,
        @EngagementDetail::jsonb, 1, now(), now(), @AuthorAccountId, @IsSealed, @OverrideCodeHash::text);
