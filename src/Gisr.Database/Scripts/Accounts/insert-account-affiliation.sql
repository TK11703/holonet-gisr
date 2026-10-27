INSERT INTO account_affiliation (id, account_id, primary_affiliation, secondary_affiliation, special_unit, prefix, operating_number)
SELECT @Id, @AccountId, @PrimaryAffiliation, @SecondaryAffiliation, @SpecialUnit, @Prefix, @OperatingNumber
WHERE (SELECT count(*) FROM account_affiliation WHERE account_id = @AccountId) < @MaxPerAccount
ON CONFLICT DO NOTHING
RETURNING id, account_id, primary_affiliation, secondary_affiliation, special_unit, prefix, operating_number, created_at;
