UPDATE record
SET title = @Title,
    affiliation = @Affiliation,
    engagement_start = @EngagementStart::date,
    engagement_end = @EngagementEnd::date,
    engagement_detail = @EngagementDetail::jsonb,
    is_sealed = @IsSealed,
    override_code_hash = CASE
        WHEN NOT @IsSealed THEN NULL
        WHEN @OverrideCodeHash::text IS NOT NULL THEN @OverrideCodeHash::text
        ELSE override_code_hash
    END,
    version = version + 1,
    updated_at = now(),
    updated_by_account_id = @UpdatedByAccountId
WHERE id = @Id AND deleted_at IS NULL AND (@IsOfficer OR subject_account_id = @CallerAccountId) AND version = @ExpectedVersion;
