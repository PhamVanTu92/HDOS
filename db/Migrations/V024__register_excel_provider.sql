-- V024: Register the excel-provider in provider_registry and update operation_registry
--       with the correct RENDER_CONTRACTS result_chart_type for each operation.
--
-- Prerequisites:
--   • V002 (provider_registry)
--   • V010 (operation_registry operations seeded)
--   • V016 (result_chart_type column added)
--   • pgcrypto extension (for bcrypt-compatible hash)
--
-- DEV CREDENTIALS:
--   client_id     : excel-provider
--   client_secret : excel-dev-secret-2024   ← set same value in excel-provider appsettings.json
--   Hash computed with pgcrypto crypt() which produces BCrypt-compatible output.

-- ── 1. Enable pgcrypto ────────────────────────────────────────────────────────

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ── 2. Register provider ──────────────────────────────────────────────────────

INSERT INTO provider_registry (
    provider_id,
    display_name,
    description,
    client_id,
    client_secret_hash,
    operations,
    chart_types,
    timeout_ms,
    status,
    priority
)
VALUES (
    'excel-provider',
    'Excel Provider',
    'Dữ liệu kinh doanh: doanh thu, tồn kho, khu vực, kênh bán hàng.',
    'excel-provider',
    crypt('excel-dev-secret-2024', gen_salt('bf', 10)),
    ARRAY[
        'report.dashboard.summary',
        'report.sales.trend',
        'report.inventory.status',
        'report.regional.performance',
        'report.channel.comparison',
        'report.product.detail',
        'report.top.performers'
    ],
    ARRAY[]::TEXT[],
    30000,
    'active',
    5
)
ON CONFLICT (provider_id) DO UPDATE
    SET client_secret_hash = EXCLUDED.client_secret_hash,
        display_name       = EXCLUDED.display_name,
        operations         = EXCLUDED.operations,
        status             = EXCLUDED.status,
        updated_at         = NOW();

-- ── 3. Set result_chart_type on existing operations ───────────────────────────
--
-- These values tell the Dashboard Designer and frontend which RENDER_CONTRACTS.md
-- widget type each operation's payload conforms to.

UPDATE operation_registry
SET result_chart_type = 'kpi_grid',      updated_at = NOW()
WHERE operation_pattern = 'report.dashboard.summary';

UPDATE operation_registry
SET result_chart_type = 'line_chart',    updated_at = NOW()
WHERE operation_pattern = 'report.sales.trend';

UPDATE operation_registry
SET result_chart_type = 'progress_rows', updated_at = NOW()
WHERE operation_pattern = 'report.inventory.status';

UPDATE operation_registry
SET result_chart_type = 'bar_chart',     updated_at = NOW()
WHERE operation_pattern = 'report.regional.performance';

UPDATE operation_registry
SET result_chart_type = 'pie_chart',     updated_at = NOW()
WHERE operation_pattern = 'report.channel.comparison';

UPDATE operation_registry
SET result_chart_type = 'simple_table',  updated_at = NOW()
WHERE operation_pattern = 'report.product.detail';

UPDATE operation_registry
SET result_chart_type = 'simple_table',  updated_at = NOW()
WHERE operation_pattern = 'report.top.performers';

-- ── 4. Relax params_schema required fields (make date params optional) ────────
--
-- The original V010 schemas had required:["fromDate","toDate"] for sales.trend
-- and required productName for product.detail. Handlers now default gracefully,
-- so we drop the strict required constraint in metadata.

UPDATE operation_registry
SET params_schema = '{
    "type": "object",
    "properties": {
        "fromDate": {"type": "string", "format": "date", "description": "ISO date, default: 30 days ago"},
        "toDate":   {"type": "string", "format": "date", "description": "ISO date, default: today"},
        "groupBy":  {"type": "string", "enum": ["day","week","month"], "default": "day"}
    },
    "additionalProperties": false
}'::jsonb,
    updated_at = NOW()
WHERE operation_pattern = 'report.sales.trend';

UPDATE operation_registry
SET params_schema = '{
    "type": "object",
    "properties": {
        "productName": {"type": "string", "description": "Filter to single product; omit for full summary"},
        "fromDate":    {"type": "string", "format": "date"},
        "toDate":      {"type": "string", "format": "date"}
    },
    "additionalProperties": false
}'::jsonb,
    updated_at = NOW()
WHERE operation_pattern = 'report.product.detail';

UPDATE operation_registry
SET params_schema = '{
    "type": "object",
    "properties": {
        "period": {"type": "string", "enum": ["today","week","month"], "default": "month"}
    },
    "additionalProperties": false
}'::jsonb,
    updated_at = NOW()
WHERE operation_pattern = 'report.channel.comparison';
