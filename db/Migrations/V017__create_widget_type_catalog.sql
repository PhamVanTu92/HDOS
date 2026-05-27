-- V017: widget_type_catalog — registry of all known chart types.
-- Each row corresponds to one chartType in RENDER_CONTRACTS.md.
-- Seeded in V018. Referenced by operation_registry.result_chart_type and widgets.chart_type.

CREATE TABLE widget_type_catalog (
    id              SERIAL          PRIMARY KEY,
    chart_type      VARCHAR(50)     NOT NULL UNIQUE,
    category        VARCHAR(30)     NOT NULL
                    CONSTRAINT chk_wtc_category
                    CHECK (category IN ('visualization', 'filter', 'layout', 'healthcare', 'ai')),
    label           VARCHAR(100)    NOT NULL,
    description     TEXT,
    icon            VARCHAR(50),
    -- Minimal JSON Schema for the data.rows[] item (used by /admin/schemas/validate)
    row_schema      JSONB           NOT NULL DEFAULT '{}',
    -- Required column names that must appear in rows[]
    required_columns TEXT[]         NOT NULL DEFAULT '{}',
    -- Optional column names
    optional_columns TEXT[]         NOT NULL DEFAULT '{}',
    -- Other chartTypes this one can be switched to client-side (same data shape)
    compatible_with  VARCHAR(50)[]  NOT NULL DEFAULT '{}',
    is_active        BOOLEAN        NOT NULL DEFAULT true,
    sort_order       INT            NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ    NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE widget_type_catalog IS
    'Registry of all RENDER_CONTRACTS.md widget types. '
    'Frontend uses this for Dashboard Designer widget palette. '
    'Backend validates result_chart_type against this table on operation registration.';

CREATE INDEX ix_wtc_category ON widget_type_catalog(category);
CREATE INDEX ix_wtc_active   ON widget_type_catalog(is_active, sort_order);
