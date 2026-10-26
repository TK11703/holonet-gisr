SELECT count(*) FROM artifact WHERE record_id = @RecordId AND removed_at IS NULL;
