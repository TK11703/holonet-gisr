CREATE TABLE artifact (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    file_name text NOT NULL CHECK (char_length(file_name) BETWEEN 1 AND 255),
    content_type text NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes BETWEEN 1 AND 26214400),
    sha256 text NOT NULL,
    blob_name text NOT NULL,
    thumbnail_blob_name text NULL,
    is_image boolean NOT NULL,
    description text NULL,
    uploaded_by_account_id uuid NOT NULL REFERENCES account (id),
    uploaded_at timestamptz NOT NULL,
    removed_at timestamptz NULL,
    removed_by_account_id uuid NULL REFERENCES account (id),
    index_status text NOT NULL DEFAULT 'pending' CHECK (index_status IN ('pending', 'indexed', 'failed', 'skipped')),
    index_detail text NULL CHECK (index_detail IN (
        'unsupported_type', 'no_text', 'ocr_disabled', 'ocr_budget_exhausted', 'ocr_page_limit', 'parse_error', 'too_large')),
    -- Images carry their alt text; no other artifact needs one.
    CONSTRAINT ck_artifact_image_description CHECK (NOT is_image OR (description IS NOT NULL AND char_length(description) BETWEEN 1 AND 500)),
    CONSTRAINT ck_artifact_description_length CHECK (description IS NULL OR char_length(description) <= 500),
    CONSTRAINT ck_artifact_removal_attribution CHECK ((removed_at IS NULL) = (removed_by_account_id IS NULL))
);

CREATE INDEX ix_artifact_record ON artifact (record_id) WHERE removed_at IS NULL;
