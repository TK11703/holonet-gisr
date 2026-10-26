SELECT account_id, profile::text AS profile_json, chain_code, updated_at
FROM account_chain_code
WHERE account_id = @AccountId;