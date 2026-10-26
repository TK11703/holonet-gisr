CREATE TABLE notification (
    id uuid PRIMARY KEY,
    recipient_account_id uuid NOT NULL REFERENCES account (id),
    kind text NOT NULL CHECK (kind IN ('service_record_created')),
    record_id uuid NULL REFERENCES record (id),
    message text NOT NULL,
    created_at timestamptz NOT NULL,
    read_at timestamptz NULL
);

CREATE INDEX ix_notification_recipient ON notification (recipient_account_id, read_at, created_at DESC);
