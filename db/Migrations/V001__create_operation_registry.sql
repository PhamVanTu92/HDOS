CREATE TABLE IF NOT EXISTS operation_registry (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    operation_pattern   TEXT        NOT NULL,
    handler_type        TEXT        NOT NULL,
    provider_id         TEXT,
    schema_version      TEXT        NOT NULL DEFAULT '1.0',
    params_schema       JSONB,
    payload_schema      JSONB,
    timeout_ms          INT         NOT NULL DEFAULT 30000,
    cacheable           BOOLEAN     NOT NULL DEFAULT FALSE,
    cache_ttl_seconds   INT,
    idempotent          BOOLEAN     NOT NULL DEFAULT TRUE,
    required_role       TEXT,
    status              TEXT        NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active', 'deprecated', 'disabled')),
    deprecation_message TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_operation_pattern UNIQUE (operation_pattern)
);

CREATE INDEX idx_op_registry_status ON operation_registry (status);
CREATE INDEX idx_op_registry_provider ON operation_registry (provider_id) WHERE provider_id IS NOT NULL;

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN NEW.updated_at = NOW(); RETURN NEW; END;
$$;

CREATE TRIGGER trg_op_registry_updated_at
    BEFORE UPDATE ON operation_registry
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
