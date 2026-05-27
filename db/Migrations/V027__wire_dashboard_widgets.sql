-- V027: Wire operation_pattern on V023 NULL-operation widgets and add new
--       widgets to the Sales Dashboard for the extended chart types.
--
-- Part 1: UPDATE the 9 widgets that had NULL operation_pattern in V023
--         (executive-dashboard + operations-center modules).
-- Part 2: INSERT new widgets into existing sales-dashboard tabs (V025).
-- Part 3: INSERT new Tab 4 "Phân tích chuyên sâu" on the sales-dashboard.
--
-- Safe to re-run: UPDATEs are idempotent; INSERTs use ON CONFLICT DO NOTHING.

-- ═════════════════════════════════════════════════════════════════════════════
-- Part 1 — Wire NULL operation_pattern on executive-dashboard widgets (V023)
-- ═════════════════════════════════════════════════════════════════════════════

-- Tab 1 (overview): Công suất Giường — use inventory progress_rows as proxy
UPDATE widgets
SET operation_pattern = 'report.inventory.status',
    params_template   = '{}'
WHERE widget_key = 'exec_bed_capacity';

-- Tab 2 (operations): Luồng Bệnh nhân Cấp cứu
UPDATE widgets
SET operation_pattern = 'report.demo.patient.flow',
    params_template   = '{}'
WHERE widget_key = 'ops_patient_flow';

-- Tab 2 (operations): Bản đồ Giường Bệnh
UPDATE widgets
SET operation_pattern = 'report.demo.bed.status',
    params_template   = '{}'
WHERE widget_key = 'ops_bed_grid';

-- Tab 2 (operations): Phòng Mổ
UPDATE widgets
SET operation_pattern = 'report.demo.room.status',
    params_template   = '{}'
WHERE widget_key = 'ops_room_status';

-- Tab 3 (alerts): Cảnh báo Lâm sàng
UPDATE widgets
SET operation_pattern = 'report.sales.alerts',
    params_template   = '{}'
WHERE widget_key = 'alerts_feed';

-- Tab 3 (alerts): NEWS2 Score
UPDATE widgets
SET operation_pattern = 'report.demo.news2',
    params_template   = '{"levelFilter": "L2,L3"}'
WHERE widget_key = 'alerts_news2';

-- operations-center (M02): Xe Cấp cứu
UPDATE widgets
SET operation_pattern = 'report.demo.map.pins',
    params_template   = '{}'
WHERE widget_key = 'ops_ems_map';

-- operations-center (M02): Phân tầng Nguy cơ
UPDATE widgets
SET operation_pattern = 'report.demo.risk.tiers',
    params_template   = '{}'
WHERE widget_key = 'ops_risk_tiers';

-- operations-center (M02): Quy trình Tiếp nhận
UPDATE widgets
SET operation_pattern = 'report.demo.flow.steps',
    params_template   = '{}'
WHERE widget_key = 'ops_flow_steps';

-- ═════════════════════════════════════════════════════════════════════════════
-- Part 2 — Add new widgets to existing sales-dashboard tabs
-- ═════════════════════════════════════════════════════════════════════════════
--
-- Tab 1 (Tổng quan,  00000000-0000-0000-0003-000000000005):
--   already occupies y=0..8 → add gauge + funnel at y=8
--
-- Tab 2 (Xu hướng,   00000000-0000-0000-0003-000000000006):
--   already occupies y=0..6 → add heatmap at y=6, scatter at y=11
--
-- Tab 3 (Sản phẩm,   00000000-0000-0000-0003-000000000007):
--   already occupies y=0..11 → add pivot_table at y=11

-- ── Tab 1 additions ───────────────────────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Gauge: achievement vs target (left half, row 4)
    ('00000000-0000-0000-0003-000000000005',
     'sales_gauge',
     'Hoàn thành mục tiêu tháng',
     'gauge',
     0, 8, 6, 4,
     'report.sales.gauge',
     '{}',
     '{"min": 0, "max": 130, "target": 100, "unit": "%"}',
     40),

    -- Funnel: sales conversion pipeline (right half, row 4)
    ('00000000-0000-0000-0003-000000000005',
     'sales_funnel',
     'Phễu chuyển đổi kinh doanh',
     'funnel',
     6, 8, 6, 4,
     'report.sales.funnel',
     '{"period": "month"}',
     '{}',
     50)

ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── Tab 2 additions ───────────────────────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Heatmap: region × weekday revenue (full width)
    ('00000000-0000-0000-0003-000000000006',
     'sales_heatmap',
     'Doanh thu theo Ngày trong Tuần & Khu vực',
     'heatmap',
     0, 6, 12, 5,
     'report.sales.heatmap',
     '{}',
     '{"colorScale": "blue-red"}',
     20),

    -- Scatter: units vs revenue bubble chart (full width)
    ('00000000-0000-0000-0003-000000000006',
     'sales_scatter',
     'Hiệu quả Sản phẩm (Đơn vị × Doanh thu)',
     'scatter',
     0, 11, 12, 5,
     'report.sales.scatter',
     '{}',
     '{}',
     30)

ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── Tab 3 additions ───────────────────────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Pivot table: Region × Channel last 30 days
    ('00000000-0000-0000-0003-000000000007',
     'sales_pivot',
     'Doanh thu theo Khu vực & Kênh bán hàng',
     'pivot_table',
     0, 11, 12, 5,
     'report.sales.pivot',
     '{}',
     '{"showTotals": true}',
     40)

ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ═════════════════════════════════════════════════════════════════════════════
-- Part 3 — New Tab 4 "Vận hành & Cảnh báo" on the sales-dashboard (V025)
-- ═════════════════════════════════════════════════════════════════════════════
--
-- Adds timeline_vertical + alert_list to keep all 7 new business chart types
-- visible on the sales-dashboard without crowding the existing tabs.

INSERT INTO module_tabs (id, module_id, slug, label, sort_order, is_default) VALUES
    ('00000000-0000-0000-0003-000000000008',
     '00000000-0000-0000-0002-000000000022',
     'van-hanh', 'Vận hành & Cảnh báo', 3, false)
ON CONFLICT (id) DO NOTHING;

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Alert feed: real-time stock + sales alerts
    ('00000000-0000-0000-0003-000000000008',
     'ops_sales_alerts',
     'Cảnh báo Kinh doanh',
     'alert_list',
     0, 0, 7, 8,
     'report.sales.alerts',
     '{}',
     '{"maxDisplay": 20, "groupBySeverity": true}',
     10),

    -- Timeline: top-revenue transactions last 7 days (right side)
    ('00000000-0000-0000-0003-000000000008',
     'ops_sales_timeline',
     'Giao dịch Nổi bật 7 ngày',
     'timeline_vertical',
     7, 0, 5, 8,
     'report.sales.timeline',
     '{"limit": 15}',
     '{}',
     20)

ON CONFLICT (tab_id, widget_key) DO NOTHING;
