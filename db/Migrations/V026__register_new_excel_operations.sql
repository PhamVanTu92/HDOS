-- V026: Register 14 new excel-provider operations in operation_registry and
--       set their result_chart_type so Dashboard Designer can resolve them.
--
-- Operations added:
--   Business charts  : gauge, heatmap, scatter, funnel, timeline_vertical,
--                      alert_list, pivot_table
--   Healthcare demo  : patient_flow_stages, bed_grid, room_status_grid,
--                      risk_tiers, flow_steps, news2_bars, map_pins
--
-- Safe to re-run: all INSERTs use ON CONFLICT DO NOTHING.

-- ── 1. Insert operations ──────────────────────────────────────────────────────

INSERT INTO operation_registry (
    operation_pattern,
    handler_type,
    provider_id,
    params_schema,
    payload_schema,
    timeout_ms,
    cacheable,
    cache_ttl_seconds,
    idempotent,
    status
)
VALUES

-- ─── Business: gauge ──────────────────────────────────────────────────────────
(
    'report.sales.gauge',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    30000, TRUE, 60, TRUE, 'active'
),

-- ─── Business: heatmap ────────────────────────────────────────────────────────
(
    'report.sales.heatmap',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "fromDate": {"type": "string", "format": "date", "description": "ISO date, default: 30 days ago"},
            "toDate":   {"type": "string", "format": "date", "description": "ISO date, default: today"}
        },
        "additionalProperties": false
    }'::jsonb,
    NULL,
    30000, TRUE, 120, TRUE, 'active'
),

-- ─── Business: scatter ────────────────────────────────────────────────────────
(
    'report.sales.scatter',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "fromDate": {"type": "string", "format": "date"},
            "toDate":   {"type": "string", "format": "date"}
        },
        "additionalProperties": false
    }'::jsonb,
    NULL,
    30000, TRUE, 120, TRUE, 'active'
),

-- ─── Business: funnel ─────────────────────────────────────────────────────────
(
    'report.sales.funnel',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "period": {"type": "string", "enum": ["week","month","quarter"], "default": "month"}
        },
        "additionalProperties": false
    }'::jsonb,
    NULL,
    30000, TRUE, 120, TRUE, 'active'
),

-- ─── Business: timeline_vertical ─────────────────────────────────────────────
(
    'report.sales.timeline',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 10}
        },
        "additionalProperties": false
    }'::jsonb,
    NULL,
    30000, TRUE, 60, TRUE, 'active'
),

-- ─── Business: alert_list ─────────────────────────────────────────────────────
(
    'report.sales.alerts',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    15000, FALSE, 0, TRUE, 'active'
),

-- ─── Business: pivot_table ────────────────────────────────────────────────────
(
    'report.sales.pivot',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "fromDate": {"type": "string", "format": "date"},
            "toDate":   {"type": "string", "format": "date"}
        },
        "additionalProperties": false
    }'::jsonb,
    NULL,
    60000, TRUE, 300, TRUE, 'active'
),

-- ─── Demo: patient_flow_stages ────────────────────────────────────────────────
(
    'report.demo.patient.flow',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
),

-- ─── Demo: bed_grid ───────────────────────────────────────────────────────────
(
    'report.demo.bed.status',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
),

-- ─── Demo: room_status_grid ───────────────────────────────────────────────────
(
    'report.demo.room.status',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
),

-- ─── Demo: risk_tiers ─────────────────────────────────────────────────────────
(
    'report.demo.risk.tiers',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
),

-- ─── Demo: flow_steps ─────────────────────────────────────────────────────────
(
    'report.demo.flow.steps',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
),

-- ─── Demo: news2_bars ─────────────────────────────────────────────────────────
(
    'report.demo.news2',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "levelFilter": {"type": "string", "description": "Comma-separated levels, e.g. \"L2,L3\". Omit for all."}
        },
        "additionalProperties": false
    }'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
),

-- ─── Demo: map_pins ───────────────────────────────────────────────────────────
(
    'report.demo.map.pins',
    'provider',
    'excel-provider',
    '{"type":"object","additionalProperties":false}'::jsonb,
    NULL,
    10000, FALSE, 0, TRUE, 'active'
)

ON CONFLICT (operation_pattern) DO NOTHING;

-- ── 2. Set result_chart_type ──────────────────────────────────────────────────

UPDATE operation_registry SET result_chart_type = 'gauge',              updated_at = NOW()
WHERE operation_pattern = 'report.sales.gauge';

UPDATE operation_registry SET result_chart_type = 'heatmap',           updated_at = NOW()
WHERE operation_pattern = 'report.sales.heatmap';

UPDATE operation_registry SET result_chart_type = 'scatter',           updated_at = NOW()
WHERE operation_pattern = 'report.sales.scatter';

UPDATE operation_registry SET result_chart_type = 'funnel',            updated_at = NOW()
WHERE operation_pattern = 'report.sales.funnel';

UPDATE operation_registry SET result_chart_type = 'timeline_vertical', updated_at = NOW()
WHERE operation_pattern = 'report.sales.timeline';

UPDATE operation_registry SET result_chart_type = 'alert_list',        updated_at = NOW()
WHERE operation_pattern = 'report.sales.alerts';

UPDATE operation_registry SET result_chart_type = 'pivot_table',       updated_at = NOW()
WHERE operation_pattern = 'report.sales.pivot';

UPDATE operation_registry SET result_chart_type = 'patient_flow_stages', updated_at = NOW()
WHERE operation_pattern = 'report.demo.patient.flow';

UPDATE operation_registry SET result_chart_type = 'bed_grid',          updated_at = NOW()
WHERE operation_pattern = 'report.demo.bed.status';

UPDATE operation_registry SET result_chart_type = 'room_status_grid',  updated_at = NOW()
WHERE operation_pattern = 'report.demo.room.status';

UPDATE operation_registry SET result_chart_type = 'risk_tiers',        updated_at = NOW()
WHERE operation_pattern = 'report.demo.risk.tiers';

UPDATE operation_registry SET result_chart_type = 'flow_steps',        updated_at = NOW()
WHERE operation_pattern = 'report.demo.flow.steps';

UPDATE operation_registry SET result_chart_type = 'news2_bars',        updated_at = NOW()
WHERE operation_pattern = 'report.demo.news2';

UPDATE operation_registry SET result_chart_type = 'map_pins',          updated_at = NOW()
WHERE operation_pattern = 'report.demo.map.pins';

-- ── 3. Also register new operations in provider_registry.operations array ─────
--
-- Keeps provider_registry.operations[] in sync so the bridge knows which
-- provider can handle each new pattern.

UPDATE provider_registry
SET operations = ARRAY[
    'report.dashboard.summary',
    'report.sales.trend',
    'report.inventory.status',
    'report.regional.performance',
    'report.channel.comparison',
    'report.product.detail',
    'report.top.performers',
    'report.sales.gauge',
    'report.sales.heatmap',
    'report.sales.scatter',
    'report.sales.funnel',
    'report.sales.timeline',
    'report.sales.alerts',
    'report.sales.pivot',
    'report.demo.patient.flow',
    'report.demo.bed.status',
    'report.demo.room.status',
    'report.demo.risk.tiers',
    'report.demo.flow.steps',
    'report.demo.news2',
    'report.demo.map.pins'
],
updated_at = NOW()
WHERE provider_id = 'excel-provider';
