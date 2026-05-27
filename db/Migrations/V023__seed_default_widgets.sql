-- V023: Seed default tabs and widgets for the Executive Dashboard module (M01).
-- executive-dashboard has 3 tabs: Overview, Operations, Alerts.
-- Other modules start empty (admin adds widgets via Dashboard Designer).

-- ── Tabs for executive-dashboard ─────────────────────────────────────────────

INSERT INTO module_tabs (id, module_id, slug, label, sort_order, is_default) VALUES
    ('00000000-0000-0000-0003-000000000001',
     '00000000-0000-0000-0002-000000000001',
     'overview', 'Tổng quan', 0, true),

    ('00000000-0000-0000-0003-000000000002',
     '00000000-0000-0000-0002-000000000001',
     'operations', 'Vận hành', 1, false),

    ('00000000-0000-0000-0003-000000000003',
     '00000000-0000-0000-0002-000000000001',
     'alerts', 'Cảnh báo', 2, false)
ON CONFLICT (id) DO NOTHING;

-- ── Tab 1: Overview — KPI Grid + Charts ──────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES
    -- Row 1: KPI Grid (full width)
    ('00000000-0000-0000-0003-000000000001',
     'exec_kpi_grid', 'Chỉ số Điều hành Hôm Nay', 'kpi_grid',
     0, 0, 12, 3,
     'report.dashboard.summary',
     '{"date": "{{today}}"}',
     '{"columns": 4}',
     10),

    -- Row 2: Revenue trend (left 8 cols)
    ('00000000-0000-0000-0003-000000000001',
     'exec_revenue_trend', 'Doanh thu 30 ngày', 'line_chart',
     0, 3, 8, 5,
     'report.sales.trend',
     '{"fromDate": "{{last30Days}}", "toDate": "{{today}}", "groupBy": "day"}',
     '{"yFormat": "currency:VND", "showLegend": true}',
     20),

    -- Row 2: Bed capacity (right 4 cols)
    ('00000000-0000-0000-0003-000000000001',
     'exec_bed_capacity', 'Công suất Giường', 'progress_rows',
     8, 3, 4, 5,
     NULL,
     '{}',
     '{"showPercent": true, "showValues": true}',
     30),

    -- Row 3: Revenue by region (left 6)
    ('00000000-0000-0000-0003-000000000001',
     'exec_region_pie', 'Doanh thu theo Vùng', 'donut_chart',
     0, 8, 6, 5,
     'report.regional.performance',
     '{"period": "month"}',
     '{}',
     40),

    -- Row 3: Channel comparison (right 6)
    ('00000000-0000-0000-0003-000000000001',
     'exec_channel_bar', 'Online vs Store', 'bar_chart',
     6, 8, 6, 5,
     'report.channel.comparison',
     '{"fromDate": "{{currentMonthStart}}", "toDate": "{{today}}"}',
     '{}',
     50)
ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── Tab 2: Operations — Bed Grid + Room Status + Patient Flow ─────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES
    -- Patient flow funnel (full width, row 1)
    ('00000000-0000-0000-0003-000000000002',
     'ops_patient_flow', 'Luồng Bệnh nhân Cấp cứu', 'patient_flow_stages',
     0, 0, 12, 3,
     NULL,
     '{}',
     '{}',
     10),

    -- Bed grid (left 8, row 2)
    ('00000000-0000-0000-0003-000000000002',
     'ops_bed_grid', 'Bản đồ Giường Bệnh', 'bed_grid',
     0, 3, 8, 6,
     NULL,
     '{}',
     '{}',
     20),

    -- Room status (right 4, row 2)
    ('00000000-0000-0000-0003-000000000002',
     'ops_room_status', 'Phòng Mổ', 'room_status_grid',
     8, 3, 4, 6,
     NULL,
     '{}',
     '{}',
     30)
ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── Tab 3: Alerts — Alert List ────────────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES
    -- Alert feed (full width)
    ('00000000-0000-0000-0003-000000000003',
     'alerts_feed', 'Cảnh báo Lâm sàng', 'alert_list',
     0, 0, 12, 8,
     NULL,
     '{}',
     '{"maxDisplay": 20}',
     10),

    -- NEWS2 overview (full width, row 2)
    ('00000000-0000-0000-0003-000000000003',
     'alerts_news2', 'NEWS2 Score — Bệnh nhân Nguy cơ Cao', 'news2_bars',
     0, 8, 12, 5,
     NULL,
     '{"levelFilter": "L2,L3"}',
     '{}',
     20)
ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── Tabs for operations-center (M02) — single Overview tab ───────────────────

INSERT INTO module_tabs (id, module_id, slug, label, sort_order, is_default) VALUES
    ('00000000-0000-0000-0003-000000000004',
     '00000000-0000-0000-0002-000000000002',
     'overview', 'Tổng quan', 0, true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES
    -- EMS map (left 8)
    ('00000000-0000-0000-0003-000000000004',
     'ops_ems_map', 'Xe Cấp cứu', 'map_pins',
     0, 0, 8, 6,
     NULL,
     '{}',
     '{"backgroundType": "city_map"}',
     10),

    -- Risk tiers (right 4)
    ('00000000-0000-0000-0003-000000000004',
     'ops_risk_tiers', 'Phân tầng Nguy cơ', 'risk_tiers',
     8, 0, 4, 6,
     NULL,
     '{}',
     '{}',
     20),

    -- Ops patient flow (full width)
    ('00000000-0000-0000-0003-000000000004',
     'ops_flow_steps', 'Quy trình Tiếp nhận', 'flow_steps',
     0, 6, 12, 3,
     NULL,
     '{}',
     '{"direction": "horizontal"}',
     30)
ON CONFLICT (tab_id, widget_key) DO NOTHING;
