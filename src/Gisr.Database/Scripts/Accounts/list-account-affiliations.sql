SELECT id, account_id, primary_affiliation, secondary_affiliation, special_unit, prefix, operating_number, created_at
FROM account_affiliation
WHERE account_id = @AccountId
ORDER BY created_at, id;
