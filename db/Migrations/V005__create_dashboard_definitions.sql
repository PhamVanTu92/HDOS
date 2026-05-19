-- Dashboard and datasource definition tables.
-- Definitions are stored as JSONB blobs versioned by an integer counter.
-- The resolver uses the version in widget cache keys to ensure stale entries
-- are structurally unreachable after a version bump.

CREATE TABLE IF NOT EXISTS datasource_definitions (
    id              BIGSERIAL    PRIMARY KEY,
    tenant_id       TEXT         NOT NULL,
    datasource_id   TEXT         NOT NULL,
    definition      JSONB        NOT NULL,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT uq_datasource_definitions_tenant_id UNIQUE (tenant_id, datasource_id)
);

CREATE INDEX IF NOT EXISTS idx_datasource_definitions_tenant
    ON datasource_definitions (tenant_id);

CREATE TABLE IF NOT EXISTS dashboard_definitions (
    id             BIGSERIAL    PRIMARY KEY,
    tenant_id      TEXT         NOT NULL,
    dashboard_code TEXT         NOT NULL,
    version        INT          NOT NULL DEFAULT 1,
    definition     JSONB        NOT NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT uq_dashboard_definitions_tenant_code UNIQUE (tenant_id, dashboard_code)
);

CREATE INDEX IF NOT EXISTS idx_dashboard_definitions_tenant
    ON dashboard_definitions (tenant_id);

-- Reuse the set_updated_at() trigger defined in V001
CREATE TRIGGER trg_datasource_definitions_updated_at
    BEFORE UPDATE ON datasource_definitions
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_dashboard_definitions_updated_at
    BEFORE UPDATE ON dashboard_definitions
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
