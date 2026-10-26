CREATE TABLE record (
    id uuid PRIMARY KEY,
    subject_account_id uuid NOT NULL REFERENCES account (id),
    author_account_id uuid NOT NULL REFERENCES account (id),
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 200),
    affiliation text NOT NULL,
    engagement_start date NULL,
    engagement_end date NULL,
    engagement_detail jsonb NOT NULL DEFAULT '{}',
    version integer NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    updated_by_account_id uuid NOT NULL REFERENCES account (id),
    CONSTRAINT ck_record_engagement_range
        CHECK (engagement_start IS NULL OR engagement_end IS NULL OR engagement_end >= engagement_start)
);

CREATE INDEX ix_record_subject_updated ON record (subject_account_id, updated_at DESC);
CREATE INDEX ix_record_affiliation ON record (affiliation);

CREATE TABLE record_incident (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    occurred_on date NOT NULL,
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 200),
    description text NULL CHECK (char_length(description) <= 4000),
    sort_order integer NOT NULL
);

CREATE INDEX ix_record_incident_record ON record_incident (record_id, sort_order);

CREATE TABLE record_award (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
    awarded_on date NOT NULL,
    issuer text NULL CHECK (char_length(issuer) <= 200),
    description text NULL CHECK (char_length(description) <= 2000),
    sort_order integer NOT NULL
);

CREATE INDEX ix_record_award_record ON record_award (record_id, sort_order);

CREATE TABLE record_revision (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    version integer NOT NULL,
    changed_by_account_id uuid NOT NULL REFERENCES account (id),
    changed_at timestamptz NOT NULL,
    snapshot jsonb NOT NULL,
    CONSTRAINT uq_record_revision_version UNIQUE (record_id, version)
);
