-- schema_definitions: stores JSON Schema bodies referenced by operation_registry.
-- schema_id is the canonical schema identifier (e.g. "dashboard.render.params/v1").
CREATE TABLE IF NOT EXISTS schema_definitions (
    id           BIGSERIAL    PRIMARY KEY,
    tenant_id    TEXT         NOT NULL,
    schema_id    TEXT         NOT NULL,
    schema_type  TEXT         NOT NULL CHECK (schema_type IN ('params', 'payload', 'render')),
    version      TEXT         NOT NULL DEFAULT '1.0',
    schema_body  JSONB        NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT uq_schema_definitions_tenant_id UNIQUE (tenant_id, schema_id)
);

CREATE INDEX IF NOT EXISTS idx_schema_definitions_tenant
    ON schema_definitions (tenant_id);

CREATE TRIGGER trg_schema_definitions_updated_at
    BEFORE UPDATE ON schema_definitions
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();  -- defined in V001
