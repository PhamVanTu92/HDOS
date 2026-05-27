-- V016: Add result_chart_type to operation_registry.
-- Links a provider operation to the widget rendering contract it satisfies.
-- NULL = raw_json fallback. Validated against widget_type_catalog (added in V017).

ALTER TABLE operation_registry
    ADD COLUMN result_chart_type VARCHAR(50) NULL;

COMMENT ON COLUMN operation_registry.result_chart_type IS
    'Declares which RENDER_CONTRACTS.md chart type this operation''s payloadJson conforms to. '
    'NULL = untyped / raw_json fallback. Must match a chart_type in widget_type_catalog.';

CREATE INDEX ix_operation_registry_chart_type
    ON operation_registry(result_chart_type)
    WHERE result_chart_type IS NOT NULL;
