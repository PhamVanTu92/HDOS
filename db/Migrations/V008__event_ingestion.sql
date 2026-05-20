-- V008__event_ingestion.sql
-- Event ingestion tables for Phase 11.
-- Adds optional JSON Schema validation per event type and a materialized
-- subscription mapping (event_type → widgets) kept in sync by EventSubscriptionSyncService.

-- Optional JSON Schema validation per event type per tenant.
CREATE TABLE event_schemas (
    tenant_id    TEXT        NOT NULL,
    event_type   TEXT        NOT NULL,
    schema_body  JSONB       NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (tenant_id, event_type)
);

COMMENT ON TABLE event_schemas IS
    'Optional JSON Schema for validating IngestEventEnvelope.payload per (tenant_id, event_type). '
    'If no row exists, payload is accepted without validation.';

CREATE TRIGGER trg_event_schemas_updated_at
    BEFORE UPDATE ON event_schemas
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Materialized mapping: event_type → widgets that must be stale-notified.
-- Populated and maintained by EventSubscriptionSyncService on each dashboard upsert.
-- FK references UNIQUE (tenant_id, dashboard_code) on dashboard_definitions;
-- ON DELETE CASCADE removes orphan rows when a dashboard is deleted (Patch 4).
CREATE TABLE event_subscriptions (
    tenant_id      TEXT        NOT NULL,
    event_type     TEXT        NOT NULL,
    dashboard_code TEXT        NOT NULL,
    widget_id      TEXT        NOT NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (tenant_id, event_type, dashboard_code, widget_id),
    FOREIGN KEY (tenant_id, dashboard_code)
        REFERENCES dashboard_definitions (tenant_id, dashboard_code)
        ON DELETE CASCADE
);

-- Primary lookup path: given (tenantId, eventType), find affected widgets.
CREATE INDEX idx_event_subscriptions_lookup
    ON event_subscriptions (tenant_id, event_type);

COMMENT ON TABLE event_subscriptions IS
    'Denormalized mapping: when (tenant_id, event_type) arrives, these widgets become stale. '
    'Populated/maintained by EventSubscriptionSyncService on dashboard upsert. '
    'ON DELETE CASCADE ensures rows are removed when the parent dashboard is deleted.';
