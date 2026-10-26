CREATE TABLE account (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    entra_object_id uuid NOT NULL,
    display_name text NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 100),
    sign_in_email text NULL,
    contact_email text NULL CHECK (char_length(contact_email) <= 254),
    notify_on_service_record boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL,
    last_sign_in_at timestamptz NOT NULL,
    CONSTRAINT uq_account_tenant_object UNIQUE (tenant_id, entra_object_id)
);
