-- V011: Seed dashboard_definitions and event_subscriptions for the main dashboard.
--
-- The frontend (useDashboard.ts) subscribes to channel "widget:main-dashboard:main-dashboard".
-- EventProcessorService looks up event_subscriptions by (tenant_id, event_type) to find
-- which widget groups to push WidgetStale to.
--
-- Without these rows the entire auto-push chain is a no-op:
--   excel-provider writes data → pg_notify → Ingestion.Api → Event.Processor
--   → query returns 0 rows → no SignalR push.
--
-- Safe to re-run: all inserts use ON CONFLICT DO NOTHING.

INSERT INTO dashboard_definitions (tenant_id, dashboard_code, version, definition)
VALUES (
    'tenant-001',
    'main-dashboard',
    1,
    '{
        "dashboardCode": "main-dashboard",
        "title": "Main Dashboard",
        "widgets": [
            {
                "widgetId": "main-dashboard",
                "chartType": "composite",
                "datasourceId": "excel-provider",
                "operation": "report.dashboard.summary",
                "subscribesTo": ["datasource.updated"]
            }
        ]
    }'::jsonb
)
ON CONFLICT (tenant_id, dashboard_code) DO UPDATE
    SET definition = EXCLUDED.definition,
        version    = dashboard_definitions.version + 1,
        updated_at = now();

-- Populate event_subscriptions directly so the sync service does not need to run.
-- This mirrors what EventSubscriptionSyncService.SyncAsync() would produce.
INSERT INTO event_subscriptions (tenant_id, event_type, dashboard_code, widget_id)
VALUES ('tenant-001', 'datasource.updated', 'main-dashboard', 'main-dashboard')
ON CONFLICT DO NOTHING;
