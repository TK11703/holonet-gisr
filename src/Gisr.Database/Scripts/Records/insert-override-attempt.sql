INSERT INTO record_override_attempt (id, record_id, account_id, attempted_at, succeeded)
VALUES (@Id, @RecordId, @AccountId, now(), @Succeeded);
