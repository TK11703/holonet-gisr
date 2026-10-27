CREATE TABLE account_affiliation (
    id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES account(id) ON DELETE CASCADE,
    primary_affiliation varchar(100) NOT NULL CHECK (char_length(primary_affiliation) >= 1),
    secondary_affiliation varchar(100) NULL,
    special_unit varchar(100) NULL,
    prefix varchar(8) NULL,
    operating_number varchar(16) NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_account_affiliation_identity
    ON account_affiliation (account_id, primary_affiliation, COALESCE(secondary_affiliation, ''), COALESCE(special_unit, ''));
