CREATE TABLE search_chunk (
    id uuid PRIMARY KEY,
    record_id uuid NOT NULL REFERENCES record (id),
    source_type text NOT NULL CHECK (source_type IN ('record', 'artifact')),
    artifact_id uuid NULL REFERENCES artifact (id),
    chunk_index integer NOT NULL CHECK (chunk_index >= 0),
    content text NOT NULL,
    embedding vector(1536) NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT ck_search_chunk_source CHECK ((source_type = 'artifact') = (artifact_id IS NOT NULL))
);

CREATE INDEX ix_search_chunk_embedding ON search_chunk USING hnsw (embedding vector_cosine_ops);
CREATE INDEX ix_search_chunk_record ON search_chunk (record_id);
CREATE INDEX ix_search_chunk_artifact ON search_chunk (artifact_id) WHERE artifact_id IS NOT NULL;

-- A durable queue: saving a record or finishing an upload enqueues a job in the same transaction.
CREATE TABLE search_index_job (
    id uuid PRIMARY KEY,
    kind text NOT NULL CHECK (kind IN ('record', 'artifact')),
    record_id uuid NOT NULL,
    artifact_id uuid NULL,
    status text NOT NULL CHECK (status IN ('pending', 'running', 'succeeded', 'failed')),
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    available_at timestamptz NOT NULL,
    last_error text NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT ck_search_index_job_target CHECK ((kind = 'artifact') = (artifact_id IS NOT NULL))
);

CREATE INDEX ix_search_index_job_claim ON search_index_job (available_at) WHERE status = 'pending';

-- Cost guard for scanned-PDF OCR; requeued_at marks the month whose budget-skipped files were already re-queued.
CREATE TABLE ocr_usage (
    month date PRIMARY KEY CHECK (month = date_trunc('month', month)::date),
    pages_used integer NOT NULL DEFAULT 0 CHECK (pages_used >= 0),
    requeued_at timestamptz NULL
);
