INSERT INTO account_chain_code (account_id, profile, chain_code)
VALUES (@AccountId, CAST(@ProfileJson AS jsonb), @ChainCode)
ON CONFLICT (account_id) DO UPDATE
SET profile = EXCLUDED.profile, chain_code = EXCLUDED.chain_code, updated_at = now()
RETURNING account_id, profile::text AS profile_json, chain_code, updated_at;