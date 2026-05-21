-- seed_new_operations.sql
-- Adds the 3 new Excel Provider operations to existing deployments.
-- Safe to re-run: ON CONFLICT (operation_pattern) DO NOTHING.
--
-- Operations added:
--   report.channel.comparison
--   report.product.detail
--   report.top.performers

-- ── Operation registry ────────────────────────────────────────────────────────

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

-- Channel comparison: Online vs Store revenue breakdown
(
    'report.channel.comparison',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "required": ["fromDate","toDate"],
        "properties": {
            "fromDate": {"type": "string", "format": "date"},
            "toDate":   {"type": "string", "format": "date"}
        },
        "additionalProperties": false
    }'::jsonb,
    '{
        "type": "object",
        "required": ["online","store","trend"],
        "properties": {
            "online": {
                "type": "object",
                "properties": {
                    "revenue":    {"type": "number"},
                    "units":      {"type": "integer"},
                    "percentage": {"type": "number"}
                }
            },
            "store": {
                "type": "object",
                "properties": {
                    "revenue":    {"type": "number"},
                    "units":      {"type": "integer"},
                    "percentage": {"type": "number"}
                }
            },
            "trend": {
                "type": "object",
                "properties": {
                    "labels": {"type": "array", "items": {"type": "string"}},
                    "online": {"type": "array", "items": {"type": "number"}},
                    "store":  {"type": "array", "items": {"type": "number"}}
                }
            }
        }
    }'::jsonb,
    60000,
    TRUE,
    300,
    TRUE,
    'active'
),

-- Product detail: per-product breakdown with regional split and daily trend
(
    'report.product.detail',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "required": ["productName","fromDate","toDate"],
        "properties": {
            "productName": {"type": "string"},
            "fromDate":    {"type": "string", "format": "date"},
            "toDate":      {"type": "string", "format": "date"}
        },
        "additionalProperties": false
    }'::jsonb,
    '{
        "type": "object",
        "required": ["productName","totalRevenue","totalUnits","avgDailyRevenue","byRegion","trend"],
        "properties": {
            "productName":     {"type": "string"},
            "totalRevenue":    {"type": "number"},
            "totalUnits":      {"type": "integer"},
            "avgDailyRevenue": {"type": "number"},
            "byRegion": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name":    {"type": "string"},
                        "revenue": {"type": "number"},
                        "units":   {"type": "integer"}
                    }
                }
            },
            "trend": {
                "type": "object",
                "properties": {
                    "labels":  {"type": "array", "items": {"type": "string"}},
                    "revenue": {"type": "array", "items": {"type": "number"}},
                    "units":   {"type": "array", "items": {"type": "integer"}}
                }
            }
        }
    }'::jsonb,
    60000,
    TRUE,
    300,
    TRUE,
    'active'
),

-- Top performers: top-5 products and regions ranked by revenue with growth%
(
    'report.top.performers',
    'provider',
    'excel-provider',
    '{
        "type": "object",
        "properties": {
            "period": {"type": "string", "enum": ["week","month","quarter"], "default": "week"}
        },
        "additionalProperties": false
    }'::jsonb,
    '{
        "type": "object",
        "required": ["topProducts","topRegions","period"],
        "properties": {
            "topProducts": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "rank":    {"type": "integer"},
                        "name":    {"type": "string"},
                        "revenue": {"type": "number"},
                        "growth":  {"type": "number"}
                    }
                }
            },
            "topRegions": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "rank":    {"type": "integer"},
                        "name":    {"type": "string"},
                        "revenue": {"type": "number"},
                        "growth":  {"type": "number"}
                    }
                }
            },
            "period": {"type": "string"}
        }
    }'::jsonb,
    30000,
    TRUE,
    120,
    TRUE,
    'active'
)

ON CONFLICT (operation_pattern) DO NOTHING;

-- ── Update provider_registry operations array ─────────────────────────────────
-- Appends the 3 new patterns only if they are not already present.

UPDATE provider_registry
SET operations = (
    SELECT ARRAY(
        SELECT DISTINCT unnest(
            operations ||
            ARRAY[
                'report.channel.comparison',
                'report.product.detail',
                'report.top.performers'
            ]
        )
        ORDER BY 1
    )
)
WHERE provider_id = 'excel-provider';
