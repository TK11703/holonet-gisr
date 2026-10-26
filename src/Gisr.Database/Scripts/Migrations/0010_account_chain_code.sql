ALTER TABLE account ADD COLUMN operating_number varchar(64);

CREATE TABLE account_chain_code (
    account_id uuid PRIMARY KEY REFERENCES account(id) ON DELETE CASCADE,
    profile jsonb NOT NULL CHECK (jsonb_typeof(profile) = 'object'),
    chain_code varchar(47) NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);