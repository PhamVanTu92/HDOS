CREATE TABLE IF NOT EXISTS provider_registry (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_id         TEXT        NOT NULL,
    display_name        TEXT        NOT NULL,
    description         TEXT,
    client_id           TEXT        NOT NULL,
    client_secret_hash  TEXT        NOT NULL,
    operations          TEXT[]      NOT NULL DEFAULT '{}',
    chart_types         TEXT[]      NOT NULL DEFAULT '{}',
    transformers        TEXT[]      NOT NULL DEFAULT '{}',
    timeout_ms          INT         NOT NULL DEFAULT 30000,
    circuit_breaker     JSONB       NOT NULL DEFAULT '{"failureThreshold":5,"windowSeconds":60,"cooldownSeconds":30}',
    priority            SMALLINT    NOT NULL DEFAULT 5
                            CHECK (priority BETWEEN 1 AND 10),
    status              TEXT        NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active', 'suspended', 'maintenance')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_provider_id UNIQUE (provider_id),
    CONSTRAINT uq_client_id   UNIQUE (client_id)
);

CREATE INDEX idx_provider_status ON provider_registry (status);

CREATE TRIGGER trg_provider_updated_at
    BEFORE UPDATE ON provider_registry
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
