CREATE TABLE IF NOT EXISTS queryable_sources (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id        TEXT        NOT NULL,
    source_name      TEXT        NOT NULL,
    schema_name      TEXT        NOT NULL DEFAULT 'public',
    table_name       TEXT        NOT NULL,
    allowed_columns  JSONB       NOT NULL DEFAULT '[]',
    sortable_columns JSONB       NOT NULL DEFAULT '[]',
    max_rows         INT         NOT NULL DEFAULT 10000,
    status           TEXT        NOT NULL DEFAULT 'active'
                         CHECK (status IN ('active', 'disabled')),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_tenant_source UNIQUE (tenant_id, source_name)
);

CREATE INDEX idx_qs_tenant ON queryable_sources (tenant_id, status);

CREATE TRIGGER trg_qs_updated_at
    BEFORE UPDATE ON queryable_sources
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
