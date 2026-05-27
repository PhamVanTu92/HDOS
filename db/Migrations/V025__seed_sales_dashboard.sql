-- V025: Seed "Bảng điều khiển kinh doanh" (Sales Dashboard) module.
--
-- Creates a new module group "Kinh doanh" and a fully wired-up module with
-- 3 tabs powered by the excel-provider operations registered in V024.
--
-- Tab 1 — Tổng quan:    kpi_grid (full-width) + pie_chart + bar_chart
-- Tab 2 — Xu hướng:     line_chart (full-width)
-- Tab 3 — Sản phẩm & Tồn kho: simple_table + progress_rows + simple_table

-- ── 1. Module group ───────────────────────────────────────────────────────────

INSERT INTO module_groups (id, slug, label, icon, sort_order) VALUES
    ('00000000-0000-0000-0001-000000000006',
     'kinh-doanh', 'Kinh doanh', 'TrendingUp', 60)
ON CONFLICT (slug) DO NOTHING;

-- ── 2. Module ─────────────────────────────────────────────────────────────────

INSERT INTO modules (id, group_id, slug, label, icon, description, required_roles, sort_order) VALUES
    ('00000000-0000-0000-0002-000000000022',
     '00000000-0000-0000-0001-000000000006',
     'sales-dashboard',
     'Bảng điều khiển kinh doanh',
     'BarChart2',
     'Doanh thu, xu hướng bán hàng, tồn kho và hiệu suất khu vực từ Excel Provider.',
     NULL,
     10)
ON CONFLICT (slug) DO NOTHING;

-- ── 3. Module tabs ────────────────────────────────────────────────────────────

INSERT INTO module_tabs (id, module_id, slug, label, sort_order, is_default) VALUES
    ('00000000-0000-0000-0003-000000000005',
     '00000000-0000-0000-0002-000000000022',
     'tong-quan', 'Tổng quan', 0, true),

    ('00000000-0000-0000-0003-000000000006',
     '00000000-0000-0000-0002-000000000022',
     'xu-huong', 'Xu hướng', 1, false),

    ('00000000-0000-0000-0003-000000000007',
     '00000000-0000-0000-0002-000000000022',
     'san-pham-ton-kho', 'Sản phẩm & Tồn kho', 2, false)
ON CONFLICT (id) DO NOTHING;

-- ── 4. Tab 1 — Tổng quan ──────────────────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Row 1: KPI dashboard summary (full width, 3 rows tall)
    ('00000000-0000-0000-0003-000000000005',
     'sales_kpi_grid',
     'Tổng quan kinh doanh hôm nay',
     'kpi_grid',
     0, 0, 12, 3,
     'report.dashboard.summary',
     '{}',
     '{"columns": 3}',
     10),

    -- Row 2 left: Channel comparison pie
    ('00000000-0000-0000-0003-000000000005',
     'sales_channel_pie',
     'Kênh bán hàng (30 ngày)',
     'pie_chart',
     0, 3, 5, 5,
     'report.channel.comparison',
     '{"period": "month"}',
     '{}',
     20),

    -- Row 2 right: Regional performance bar
    ('00000000-0000-0000-0003-000000000005',
     'sales_region_bar',
     'Thực tế vs Mục tiêu theo Khu vực',
     'bar_chart',
     5, 3, 7, 5,
     'report.regional.performance',
     '{"period": "month"}',
     '{"showLegend": true}',
     30)

ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── 5. Tab 2 — Xu hướng ───────────────────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Full-width line chart (30-day daily revenue trend)
    ('00000000-0000-0000-0003-000000000006',
     'sales_trend_line',
     'Xu hướng doanh thu 30 ngày',
     'line_chart',
     0, 0, 12, 6,
     'report.sales.trend',
     '{"groupBy": "day"}',
     '{"yFormat": "currency:VND", "showLegend": false, "smooth": true}',
     10)

ON CONFLICT (tab_id, widget_key) DO NOTHING;

-- ── 6. Tab 3 — Sản phẩm & Tồn kho ───────────────────────────────────────────

INSERT INTO widgets
    (tab_id, widget_key, title, chart_type,
     grid_x, grid_y, grid_w, grid_h,
     operation_pattern, params_template, visual_config, sort_order)
VALUES

    -- Left: Product sales summary table
    ('00000000-0000-0000-0003-000000000007',
     'sales_product_table',
     'Doanh thu sản phẩm (30 ngày)',
     'simple_table',
     0, 0, 6, 6,
     'report.product.detail',
     '{}',
     '{"striped": true, "compact": true}',
     10),

    -- Right: Inventory status progress bars
    ('00000000-0000-0000-0003-000000000007',
     'sales_inventory',
     'Tình trạng tồn kho',
     'progress_rows',
     6, 0, 6, 6,
     'report.inventory.status',
     '{}',
     '{"showPercent": true, "showValues": true}',
     20),

    -- Bottom: Top performers table (full width)
    ('00000000-0000-0000-0003-000000000007',
     'sales_top_performers',
     'Top sản phẩm bán chạy',
     'simple_table',
     0, 6, 12, 5,
     'report.top.performers',
     '{"period": "month", "limit": 10}',
     '{"striped": true}',
     30)

ON CONFLICT (tab_id, widget_key) DO NOTHING;
