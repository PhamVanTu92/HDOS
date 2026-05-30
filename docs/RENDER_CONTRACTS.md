# RENDER_CONTRACTS.md — Widget Render Payload Contracts
> Version: 7.0 | Audience: Frontend team + Provider teams | Last updated: 2026-05-27

This document is the **frontend rendering contract**. Frontend reads `chartType` from each widget payload and renders accordingly. Backend NEVER sends business-specific shapes — everything conforms to one of the 28 contracts below.

**Rule**: if `chartType` is unknown, render `raw_json` fallback (§10.2). Never crash.

---

## Table of Contents

1. [Common Envelope](#1-common-envelope)
   - 1.1 `meta` block (every payload)
   - 1.2 Widget envelope wrapper
   - 1.3 Empty / error state conventions
   - 1.4 Null values in data

2. [DashboardRenderPayload](#2-dashboardrenderpayload)
   - 2.1 Full schema
   - 2.2 Filter-widget binding
   - 2.3 Refresh policy fields

3. [Visualization Widgets (10)](#3-visualization-widgets)
   - 3.1 `line_chart` / `bar_chart` / `area_chart` — compatible group
   - 3.2 `pie_chart` / `donut_chart` — compatible group
   - 3.3 `kpi`
   - 3.4 `gauge`
   - 3.5 `heatmap`
   - 3.6 `scatter`
   - 3.7 `advanced_table` (server-side paginated)
   - 3.8 `simple_table` (client-side)
   - 3.9 `pivot_table`
   - 3.10 `funnel`

4. [Filter Widgets (4)](#4-filter-widgets)
   - 4.1 `filter_dropdown`
   - 4.2 `filter_date_range`
   - 4.3 `filter_slider`
   - 4.4 `filter_search`
   - 4.5 Filter interaction protocol

5. [Layout Widgets (2)](#5-layout-widgets)
   - 5.1 `text_widget`
   - 5.2 `tab_container`

6. [Interactions & Drill-down](#6-interactions--drill-down)
   - 6.1 `onClickDataPoint` — open_dashboard
   - 6.2 `drillPath` hierarchy
   - 6.3 Template token reference
   - 6.4 `widget.drillContext` protocol

7. [Chart Type Switching](#7-chart-type-switching)
   - 7.1 Compatible groups
   - 7.2 Client-side switch (no backend call)
   - 7.3 Validation at definition save time

8. [Server-side Table Protocol](#8-server-side-table-protocol)
   - 8.1 `_table` params in `widget.render`
   - 8.2 Pagination fields in response
   - 8.3 Computed columns (7 pre-defined transforms)

9. [Pre-defined Computed Transforms](#9-pre-defined-computed-transforms)

10. [External Provider Chart Types](#10-external-provider-chart-types)
    - 10.1 How providers register custom chart types
    - 10.2 `raw_json` fallback for unknown types
    - 10.3 `resultChartType` field in operation registry

11. [Healthcare & Clinical Widgets (12)](#11-healthcare--clinical-widgets)
    - 11.1 `kpi_grid` — KPI card grid
    - 11.2 `progress_rows` — labeled progress bars
    - 11.3 `flow_steps` — horizontal/vertical step flow
    - 11.4 `timeline_vertical` — vertical event timeline
    - 11.5 `alert_list` — L1/L2/L3 alert feed
    - 11.6 `bed_grid` — department bed status grid
    - 11.7 `room_status_grid` — OR/ICU room status
    - 11.8 `map_pins` — floor plan / city map pins
    - 11.9 `patient_flow_stages` — patient journey funnel
    - 11.10 `risk_tiers` — 4-tier risk stratification
    - 11.11 `news2_bars` — NEWS2 score per-patient bars
    - 11.12 `chat_panel` — AI chatbot widget

12. [JSON Schema Registry](#12-json-schema-registry)

13. [Changelog](#13-changelog)

---

## 1. Common Envelope

### 1.1 `meta` block — present in every widget payload

```json
{
  "meta": {
    "renderContractVersion": "1.0",
    "generatedAt": "2026-05-27T10:00:00Z",
    "fromCache": false,
    "elapsedMs": 47,
    "subscribeChannel": "widget:sales_dashboard_2025:revenue_chart"
  }
}
```

| Field | Type | Description |
|---|---|---|
| `renderContractVersion` | string | Semver of this contract. Breaking changes = new `chartType` name or major bump |
| `generatedAt` | ISO 8601 | Server time at payload generation |
| `fromCache` | boolean | True if served from Redis cache |
| `elapsedMs` | number | Backend processing time |
| `subscribeChannel` | string | SignalR channel to subscribe for `WidgetStale` events |

### 1.2 Widget envelope wrapper

Every `widget.render` response is wrapped in:

```json
{
  "widgetId": "revenue_chart",
  "chartType": "line_chart",
  "title": "Doanh thu 12 tháng",
  "subtitle": null,
  "visualConfig": { },
  "interactions": { },
  "data": { },
  "meta": { }
}
```

| Field | Description |
|---|---|
| `widgetId` | Matches definition |
| `chartType` | Determines which contract applies to `data` |
| `visualConfig` | Passthrough from definition — frontend interprets (colors, formats, axis labels) |
| `interactions` | Drill-down config — see §6 |
| `data` | Chart-type-specific payload — see §3–11 |
| `meta` | See §1.1 |

### 1.3 Empty / error state

```json
{
  "widgetId": "revenue_chart",
  "chartType": "line_chart",
  "title": "...",
  "data": { "series": [] },
  "isEmpty": true,
  "error": null,
  "meta": { ... }
}
```

- `isEmpty: true` + schema-valid empty `data` → render empty state (e.g. "No data")
- `error: { code, message }` + `data: null` → render error state. Do NOT crash.
- A dashboard-level render failure of one widget does NOT fail the dashboard — other widgets render normally.

### 1.4 Null values in data

- `y: null` in a series point = no data for this point → render as gap, not zero
- `color: null` = use default palette color
- Never assume non-null for optional fields

---

## 2. DashboardRenderPayload

```json
{
  "dashboardId": "uuid",
  "dashboardCode": "sales_dashboard_2025",
  "title": "Dashboard doanh thu 2025",
  "description": null,
  "tenantId": "tenant-001",
  "version": 3,
  "renderedAt": "2026-05-27T10:00:00Z",
  "refreshPolicy": {
    "mode": "interval",
    "intervalSeconds": 30,
    "debounceMs": 500
  },
  "appliedFilters": {
    "year": 2025,
    "region": "north"
  },
  "appliedDateRange": { "from": "2025-01-01", "to": "2025-12-31" },
  "widgets": [ ]
}
```

- `widgets` is an array of widget envelopes (§1.2), one per non-layout widget on the dashboard
- `appliedFilters` = filters actually applied (may differ from requested if defaults kicked in)
- `refreshPolicy.debounceMs`: frontend should debounce filter changes before sending `dashboard.render`
- Filter widgets appear in `widgets[]` with their current values pre-populated

---

## 3. Visualization Widgets

### 3.1 `line_chart` / `bar_chart` / `area_chart`

These three share the **same data shape** (compatible group — see §7). Frontend may display any of them without calling the backend again.

```json
{
  "data": {
    "series": [
      {
        "name": "Revenue",
        "data": [
          { "x": "2025-01", "y": 1500000 },
          { "x": "2025-02", "y": 1650000 }
        ]
      },
      {
        "name": "Cost",
        "data": [
          { "x": "2025-01", "y": 800000 },
          { "x": "2025-02", "y": 850000 }
        ]
      }
    ],
    "axes": {
      "x": { "type": "category", "label": "Month", "format": "yyyy-MM" },
      "y": { "type": "number",   "label": "Amount (VND)", "format": "currency:VND" },
      "y2": null
    },
    "annotations": [
      { "x": "2025-06-15", "label": "Campaign launch", "color": "#ff0000" }
    ]
  }
}
```

**Field reference:**
- `series[].data[].x`: string (category/time) or number
- `series[].data[].y`: number or null (null = gap)
- `axes.x.type`: `"category"` | `"time"` | `"number"`
- `axes.y2`: secondary axis for combo charts (optional)
- `annotations`: event markers on the x-axis (optional, may be empty array)

**Edge cases:**
- Empty: `series: []`
- Single point: render as dot (line_chart) or single bar
- All nulls in a series: series still appears in legend; line has gaps for all points
- Missing `y2`: do not render secondary axis

**Interactions**: `onClickDataPoint` supported. `clicked.x` = the x value, `clicked.series` = series name.

---

### 3.2 `pie_chart` / `donut_chart`

```json
{
  "data": {
    "slices": [
      { "label": "North",   "value": 1500000, "color": null },
      { "label": "South",   "value": 1200000, "color": "#4F46E5" },
      { "label": "Central", "value":  800000, "color": null }
    ],
    "total": 3500000,
    "valueFormat": "currency:VND"
  }
}
```

- `color: null` = use default palette
- `total` is pre-computed server-side (sum of slice values)
- For `donut_chart`: same payload; frontend renders center hole
- **Edge cases:** empty `slices: []`; single slice = full circle

**Interactions**: `onClickDataPoint` supported. `clicked.label` = slice label, `clicked.value` = value.

---

### 3.3 `kpi`

```json
{
  "data": {
    "value": 18500000,
    "format": "currency:VND",
    "label": "Total revenue",
    "comparison": {
      "previousValue": 15200000,
      "delta": 3300000,
      "deltaPercent": 21.7,
      "direction": "up",
      "isGood": true,
      "periodLabel": "vs last month"
    },
    "sparkline": [12000000, 13500000, 14800000, 15200000, 18500000]
  }
}
```

- `comparison`: null if no comparison period configured
- `comparison.isGood`: server-side opinion (up is good for revenue; up is bad for error rate). Frontend uses for color (green/red).
- `sparkline`: 7–30 numeric values, oldest first. Null if not configured.
- **Edge cases:** `value: null` = show "—"; `sparkline: []` = no sparkline

---

### 3.4 `gauge`

```json
{
  "data": {
    "value": 75,
    "min": 0,
    "max": 100,
    "unit": "%",
    "thresholds": [
      { "from": 0,  "to": 50,  "color": "red",    "label": "Low"    },
      { "from": 50, "to": 80,  "color": "yellow", "label": "Medium" },
      { "from": 80, "to": 100, "color": "green",  "label": "High"   }
    ],
    "target": 90
  }
}
```

- `target`: optional reference line on gauge
- `thresholds`: contiguous ranges covering `[min, max]`. Frontend fills color based on `value`.
- **Edge cases:** `value < min` or `value > max` = clamp and show warning tooltip

---

### 3.5 `heatmap`

```json
{
  "data": {
    "xLabels": ["Mon", "Tue", "Wed", "Thu", "Fri"],
    "yLabels": ["09:00", "10:00", "11:00", "12:00"],
    "cells": [
      { "x": "Mon", "y": "09:00", "value": 42,  "tooltip": "42 transactions" },
      { "x": "Mon", "y": "10:00", "value": null, "tooltip": "No data" }
    ],
    "valueRange": { "min": 0, "max": 100 },
    "colorScale": "sequential"
  }
}
```

- `cells` is sparse — only non-null values are included. Missing (x, y) combinations = no cell rendered (treat as null).
- `colorScale`: `"sequential"` | `"diverging"` | `"categorical"`
- `value: null` = render as empty cell with distinct visual treatment

---

### 3.6 `scatter`

```json
{
  "data": {
    "series": [
      {
        "name": "Customer segment A",
        "points": [
          { "x": 100, "y": 200, "size": 15, "label": "C-001", "color": null }
        ]
      }
    ],
    "axes": {
      "x": { "label": "Spend", "format": "currency:USD" },
      "y": { "label": "Frequency", "format": "number" }
    }
  }
}
```

- `size`: bubble size (optional); null = default size
- `label`: tooltip label for the point
- **Edge cases:** empty `series: []`; empty `points: []` within a series

**Interactions**: `onClickDataPoint` supported. `clicked.label` = point label.

---

### 3.7 `advanced_table` (server-side paginated)

```json
{
  "data": {
    "columns": [
      {
        "key": "month",
        "label": "Tháng",
        "type": "string",
        "sortable": true,
        "filterable": true,
        "filterType": "text",
        "format": null,
        "width": 120,
        "frozen": "left",
        "visible": true,
        "align": "left"
      },
      {
        "key": "revenue",
        "label": "Doanh thu",
        "type": "number",
        "sortable": true,
        "filterable": true,
        "filterType": "range",
        "format": "currency:VND",
        "aggregation": "sum",
        "align": "right"
      },
      {
        "key": "delta",
        "label": "Δ",
        "type": "number",
        "computed": "delta_from_previous",
        "computedOn": "revenue",
        "format": "percent:1",
        "sortable": false,
        "filterable": false
      }
    ],
    "rows": [
      { "month": "2025-01", "revenue": 1500000, "delta": null },
      { "month": "2025-02", "revenue": 1650000, "delta": 0.10 }
    ],
    "pagination": {
      "mode": "server",
      "page": 1,
      "pageSize": 50,
      "totalRows": 1247,
      "totalPages": 25
    },
    "sort": [
      { "key": "revenue", "direction": "desc" }
    ],
    "filters": [
      { "key": "region", "op": "in", "values": ["north", "south"] }
    ],
    "footer": {
      "show": true,
      "totals": { "revenue": 18500000 }
    },
    "exportHint": ["csv", "xlsx"]
  }
}
```

**Column types:** `"string"` | `"number"` | `"date"` | `"boolean"` | `"badge"` | `"currency"`

**Filter types per column:** `"text"` | `"range"` | `"select"` | `"date"`

**Computed columns:** `computed` field contains one of the 7 pre-defined transforms (see §9). `computedOn` = the source column. These are computed server-side and appear as regular values in `rows`.

**Server-side pagination protocol** (see §8 for full request format):
- User changes page/sort/filter → frontend sends `widget.render` with `_table` in params
- Backend returns updated `data` with same `columns` but new `rows` + updated `pagination`

**Frozen columns:** `frozen: "left"` | `"right"` | `null`

**Edge cases:** `rows: []` with `pagination.totalRows: 0` = empty state; `footer.totals` keys are a subset of numeric columns

---

### 3.8 `simple_table` (client-side)

Same `columns` + `rows` shape but:
```json
{
  "data": {
    "columns": [ /* same schema */ ],
    "rows": [ /* all rows, max 1000 */ ],
    "pagination": {
      "mode": "client",
      "totalRows": 342
    }
  }
}
```

- `pagination.mode = "client"` → frontend handles all sorting/filtering/pagination in memory
- Max 1000 rows. Datasource must enforce this server-side.
- `computed` columns ARE still pre-computed server-side and included in `rows`

---

### 3.9 `pivot_table`

```json
{
  "data": {
    "rowDimensions": [
      { "key": "region",  "label": "Khu vực" },
      { "key": "channel", "label": "Kênh" }
    ],
    "columnDimensions": [
      { "key": "quarter", "label": "Quý" }
    ],
    "measures": [
      { "key": "revenue", "label": "Doanh thu", "aggregate": "sum",   "format": "currency:VND" },
      { "key": "orders",  "label": "Đơn hàng",  "aggregate": "count", "format": "number" }
    ],
    "cells": [
      {
        "rowKey": ["north", "online"],
        "columnKey": ["Q1"],
        "values": { "revenue": 1500000, "orders": 234 }
      },
      {
        "rowKey": ["north", "offline"],
        "columnKey": ["Q1"],
        "values": { "revenue": 980000, "orders": 156 }
      }
    ],
    "rowTotals": [
      { "rowKey": ["north"], "values": { "revenue": 5200000, "orders": 812 } }
    ],
    "columnTotals": [
      { "columnKey": ["Q1"], "values": { "revenue": 12400000, "orders": 1923 } }
    ],
    "grandTotal": { "revenue": 48500000, "orders": 7284 }
  }
}
```

- `cells` are all non-null pivot intersections
- Missing intersections (no data for rowKey × columnKey) = null cell
- `rowTotals` aggregate across all column dimensions for a given row combination
- `columnTotals` aggregate across all row dimensions for a given column value

---

### 3.10 `funnel`

```json
{
  "data": {
    "steps": [
      { "label": "Visited",   "value": 10000, "percentOfStart": 100.0, "dropRate": null },
      { "label": "Signed up", "value":  3000, "percentOfStart":  30.0, "dropRate": 70.0 },
      { "label": "Purchased", "value":   450, "percentOfStart":   4.5, "dropRate": 85.0 }
    ]
  }
}
```

- `dropRate`: percentage of previous step that did NOT proceed. Null for first step.
- `percentOfStart`: percentage relative to step 0 value.
- **Edge cases:** `steps: []`; single step (render as 100% bar)

---

## 4. Filter Widgets

Filter widgets drive dashboard-level filters. When a user changes a filter widget, frontend re-sends `dashboard.render` with the new filter value. See §4.5 for interaction protocol.

### 4.1 `filter_dropdown`

```json
{
  "data": {
    "filterKey": "region",
    "label": "Khu vực",
    "options": [
      { "value": "north",   "label": "Miền Bắc", "count": 1234 },
      { "value": "south",   "label": "Miền Nam",  "count": 2456 },
      { "value": "central", "label": "Miền Trung", "count": 891 }
    ],
    "currentValue": "north",
    "multiSelect": false,
    "searchable": true,
    "clearable": true
  }
}
```

- `options` populated by `widget.filterOptions` call (see §4.5). On dashboard load, this array is pre-filled from the datasource.
- `currentValue`: string (single-select) or string[] (multi-select)
- `count`: how many records match this option (optional; null if not computed)
- **Search**: when `searchable: true`, call `widget.filterOptions` with `search` param on user input (debounce 300ms)

### 4.2 `filter_date_range`

```json
{
  "data": {
    "filterKey": "dateRange",
    "label": "Khoảng thời gian",
    "currentValue": { "from": "2025-01-01", "to": "2025-12-31" },
    "presets": [
      { "label": "Hôm nay",      "value": "today" },
      { "label": "7 ngày qua",   "value": "last_7d" },
      { "label": "Tháng này",    "value": "this_month" },
      { "label": "Quý này",      "value": "this_quarter" },
      { "label": "Năm nay",      "value": "this_year" }
    ],
    "minDate": "2020-01-01",
    "maxDate": null
  }
}
```

- Presets are resolved server-side when `dashboard.render` is called with a preset value. Frontend always receives resolved ISO 8601 dates in `currentValue`.
- `maxDate: null` = no upper bound

### 4.3 `filter_slider`

```json
{
  "data": {
    "filterKey": "amount",
    "label": "Khoảng giá trị",
    "min": 0,
    "max": 10000000,
    "step": 100000,
    "currentValue": { "from": 1000000, "to": 5000000 },
    "format": "currency:VND",
    "rangeMode": true
  }
}
```

- `rangeMode: false` = single value slider; `currentValue` is a number not an object

### 4.4 `filter_search`

```json
{
  "data": {
    "filterKey": "search",
    "label": "Tìm kiếm",
    "currentValue": "",
    "placeholder": "Nhập tên khách hàng, mã đơn..."
  }
}
```

- Debounce user input 500ms before sending `dashboard.render`

### 4.5 Filter interaction protocol

```
User changes filter_dropdown:
  1. Frontend updates local filter state
  2. After debounceMs (from refreshPolicy), calls:
       POST /api/v1/requests
       { operation: "dashboard.render", params: { dashboardCode, filters: { ...allFilters } } }
  3. All visualization widgets re-render with new filter context
  4. Filter widgets re-render to show new currentValue

Filter_dropdown with dynamic options (searchable):
  On user input (debounced 300ms):
       POST /api/v1/requests
       { operation: "widget.filterOptions",
         params: { dashboardCode, widgetId: "region_filter", search: "miền" } }
  Response via SignalR → update options list in dropdown
```

---

## 5. Layout Widgets

Layout widgets have NO datasource. They render structural content only.

### 5.1 `text_widget`

```json
{
  "data": {
    "content": "## Doanh thu Q4\n\nMục tiêu: **20 tỷ VND**\n\nTrạng thái: {{revenueStatus}}",
    "renderMode": "markdown",
    "templateVariables": {
      "revenueStatus": "Đạt 92% mục tiêu"
    }
  }
}
```

- Template variables resolved server-side from dashboard filters / pre-computed values
- `content` already has variables substituted (backend delivers final markdown)
- `templateVariables` is informational — shows what was substituted
- `renderMode`: `"markdown"` | `"html"` (html is sanitized server-side)
- **Security**: backend ONLY substitutes whitelisted tokens (`{{filters.x}}`, `{{vars.y}}`). NO arbitrary expression evaluation. Frontend receives safe, pre-substituted content.

### 5.2 `tab_container`

```json
{
  "data": {
    "tabs": [
      {
        "id": "overview",
        "label": "Tổng quan",
        "widgetIds": ["revenue_kpi", "revenue_chart", "region_pie"],
        "default": true
      },
      {
        "id": "details",
        "label": "Chi tiết",
        "widgetIds": ["sales_table", "regional_breakdown"],
        "default": false
      },
      {
        "id": "forecasts",
        "label": "Dự báo",
        "widgetIds": ["forecast_chart"],
        "default": false
      }
    ]
  }
}
```

- `tab_container` is a **layout reference** only. The actual widget data for widgets in each tab lives in `widgets[]` at the dashboard level as siblings.
- Frontend uses `widgetIds` to know which widgets to render in each tab.
- Widgets in non-default tabs: backend pre-renders them if fast enough; otherwise they are present in `widgets[]` with `isEmpty: true` and are lazy-loaded when the tab is opened via `widget.render`.

---

## 6. Interactions & Drill-down

Every visualization widget supports an optional `interactions` block.

```json
{
  "interactions": {
    "onClickDataPoint": {
      "type": "open_dashboard",
      "targetDashboardCode": "sales_detail",
      "filterMapping": {
        "year":   "{{clicked.year}}",
        "region": "{{clicked.region}}",
        "month":  "{{clicked.x}}"
      },
      "openMode": "drilldown"
    },
    "onClickRow": null,
    "onClickCell": null,
    "drillPath": [
      { "level": "year",    "field": "year",    "label": "Năm" },
      { "level": "quarter", "field": "quarter", "label": "Quý" },
      { "level": "month",   "field": "month",   "label": "Tháng" }
    ]
  }
}
```

### 6.1 `onClickDataPoint`

- `type: "open_dashboard"` — navigate to target dashboard with resolved filters
- `openMode`: `"drilldown"` (push breadcrumb) | `"new_tab"` | `"replace"`

### 6.2 `drillPath`

Defines a hierarchy. At level N, click drills to level N+1 with clicked value as filter. At deepest level, `onClickDataPoint` fires.

### 6.3 Template token reference

Available in `filterMapping` values:

| Token | Resolves to |
|---|---|
| `{{clicked.x}}` | X-axis value of clicked data point |
| `{{clicked.y}}` | Y-axis value |
| `{{clicked.label}}` | Label of clicked slice/bar/point |
| `{{clicked.<field>}}` | Any field from the clicked row (table/pivot) |
| `{{filters.<key>}}` | Current dashboard filter value |
| `{{user.tenantId}}` | Current user's tenant |

### 6.4 `widget.drillContext` protocol

Before navigating to target dashboard, call `widget.drillContext` to resolve tokens server-side and validate:

```json
{
  "operation": "widget.drillContext",
  "params": {
    "sourceDashboard": "sales_dashboard_2025",
    "widgetId": "revenue_chart",
    "clickedData": { "x": "2025-06", "series": "Revenue", "y": 1650000 },
    "targetDashboard": "sales_detail"
  }
}
```

Response via SignalR:
```json
{
  "resolvedFilters": { "year": 2025, "month": "2025-06", "region": "north" },
  "targetDashboardCode": "sales_detail",
  "valid": true
}
```

Use `resolvedFilters` as the `filters` param in `dashboard.render` for the target.

---

## 7. Chart Type Switching

### 7.1 Compatible groups (enforced at definition save time)

| Group | Types | Notes |
|---|---|---|
| Time/category | `line_chart`, `bar_chart`, `area_chart` | Same `data.series` shape |
| Part-of-whole | `pie_chart`, `donut_chart` | Same `data.slices` shape |
| Tabular | `simple_table`, `advanced_table` | Only within size limits |
| Progress/capacity | `progress_rows`, `bar_chart` (horizontal) | `bar_chart` with `axes.x.type: "number"` |
| Patient journey | `patient_flow_stages`, `funnel` | Same step/stage concept; `funnel` shows drop rates |
| KPI variants | `kpi_grid`, `kpi` | `kpi_grid` renders multiple `kpi`-style items in a grid |

### 7.2 Client-side switch (no backend call)

When user selects a different `chartType` within the same compatible group:
- Frontend re-renders with the same `data` payload received
- NO need to call `widget.render` again
- The render payload's `chartType` field does NOT change — only frontend's display mode changes

### 7.3 Validation at definition save time

Saving a `WidgetDefinition` with `allowedChartTypes: ["line_chart", "pie_chart"]` → **400 error** (different groups). Backend enforces this at metadata save time, so the frontend never receives invalid switching configs.

---

## 8. Server-side Table Protocol

### 8.1 `_table` params in `widget.render`

```json
{
  "operation": "widget.render",
  "params": {
    "dashboardCode": "sales_dashboard_2025",
    "widgetId": "sales_table",
    "filters": {
      "year": 2025,
      "region": "north",
      "_table": {
        "page": 2,
        "pageSize": 50,
        "sort": [{ "key": "revenue", "direction": "desc" }],
        "filters": [
          { "key": "region", "op": "=", "value": "north" }
        ]
      }
    }
  }
}
```

- `_table` is only honoured for `advanced_table` widgets
- `sort` is an array; first element is primary sort; subsequent are tie-breakers
- `filters[].op`: `"="` | `"!="` | `">"` | `">="` | `"<"` | `"<="` | `"in"` | `"contains"`

### 8.2 Pagination fields in response

Backend always echoes back the effective `page`, `pageSize`, `sort`, and `filters` in `data.pagination`, `data.sort`, and `data.filters` so frontend can sync UI state.

### 8.3 Computed columns

For `advanced_table`, computed columns are calculated server-side (on the full result set, before pagination), then only the current page's rows are returned. The computed values appear as regular fields in `rows`.

---

## 9. Pre-defined Computed Transforms

Available as `column.computed` values in `advanced_table` and `simple_table` column definitions:

| Transform | Formula | Notes |
|---|---|---|
| `delta_from_previous` | `value(n) - value(n-1)` | Null for first row |
| `percent_change_from_previous` | `(value(n) - value(n-1)) / abs(value(n-1))` | Null for first row or zero denominator |
| `percent_of_total` | `value(n) / sum(all values in column)` | Based on current page for server-side; full result set for client-side |
| `running_total` | Cumulative sum up to row n | Respects current sort order |
| `moving_average_3` | Average of current row + 2 previous | Null for first 2 rows |
| `moving_average_7` | Average of current row + 6 previous | Null for first 6 rows |
| `rank` | 1-based rank within column (descending) | Ties share rank; no skip |

**Backend:** computed for `advanced_table` before pagination. `simple_table` also computes server-side (simpler — full dataset available).

**Frontend:** for `simple_table`, frontend MAY re-compute on client-side sort (optional, since backend already provides values).

---

## 10. External Provider Chart Types

### 10.1 How providers register custom chart types

Provider declares `chartTypes: ["waterfall_chart"]` at registration. The chart type must have a registered JSON Schema payload definition via `POST /api/v1/admin/schemas`.

### 10.2 `raw_json` fallback for unknown types

If frontend encounters an unknown `chartType`, render the `raw_json` fallback:

```json
{
  "chartType": "raw_json",
  "data": { }
}
```

Display as pretty-printed JSON. Never crash on unknown chart types. Backend will never intentionally send `chartType: "raw_json"` — it is only a frontend fallback label.

### 10.3 `resultChartType` field in operation registry

Every operation registered via `POST /api/v1/admin/operations` may include a `resultChartType` field declaring which widget type its `payloadJson` conforms to:

```json
{
  "operationPattern": "report.dashboard.summary",
  "handlerType": "external",
  "providerId": "excel-provider",
  "resultChartType": "kpi_grid",
  "payloadSchema": { ... }
}
```

- The platform validates that `payloadSchema` is consistent with the declared `resultChartType`'s JSON Schema (from `widget_type_catalog` table).
- If `resultChartType` is null: the operation returns arbitrary JSON rendered as `raw_json`.
- The Dashboard Designer uses `resultChartType` to offer only compatible widget placements.
- Retrieve all operations compatible with a given chart type: `GET /api/v1/admin/operations?resultChartType=kpi_grid`

---

## 11. Healthcare & Clinical Widgets

These 12 widget types are purpose-built for clinical and hospital operational data. They follow the same widget envelope (§1.2) and empty/error state conventions (§1.3).

> **Provider note**: When your operation serves one of these widget types, set `resultChartType` in the operation registry (§10.3) so the Dashboard Designer can surface it correctly.

---

### 11.1 `kpi_grid`

A responsive grid of KPI cards. Each item is a single `kpi`-equivalent cell. Use when you need 2–6 KPIs on one row without individual widget grid slots.

```json
{
  "data": {
    "columns": 4,
    "items": [
      {
        "id": "revenue",
        "label": "Doanh thu hôm nay",
        "value": 1850000000,
        "format": "currency:VND",
        "comparison": {
          "deltaPercent": 12.5,
          "direction": "up",
          "isGood": true,
          "periodLabel": "vs hôm qua"
        },
        "sparkline": [1400000000, 1520000000, 1680000000, 1750000000, 1850000000],
        "icon": "trending_up",
        "variant": "default"
      },
      {
        "id": "beds_occupied",
        "label": "Giường đang dùng",
        "value": 312,
        "format": "number",
        "comparison": { "deltaPercent": 3.2, "direction": "up", "isGood": false, "periodLabel": "vs hôm qua" },
        "sparkline": null,
        "icon": "bed",
        "variant": "warning"
      }
    ]
  }
}
```

| Field | Type | Description |
|---|---|---|
| `columns` | `2\|3\|4\|5` | Grid column count (frontend may override for responsive) |
| `items[].id` | string | Unique ID within this widget |
| `items[].label` | string | Display label |
| `items[].value` | number\|string | Primary metric value |
| `items[].format` | string | Same format strings as `kpi` (§3.3) |
| `items[].comparison` | object\|null | Delta vs previous period. Same shape as `kpi.comparison` minus `previousValue` and `delta` |
| `items[].sparkline` | number[]\|null | Trend values (oldest first); null = no sparkline |
| `items[].icon` | string\|null | Icon hint (frontend maps to icon library) |
| `items[].variant` | `"default"\|"success"\|"warning"\|"danger"\|"info"` | Card accent color |

**Compatible group**: `kpi_grid` ↔ `kpi` (§7.1). Switching renders items as individual `kpi` widgets.

---

### 11.2 `progress_rows`

Labeled rows each showing a progress bar. Use for bed occupancy, OR utilization, inventory levels.

```json
{
  "data": {
    "rows": [
      {
        "id": "dept_noi",
        "label": "Khoa Nội",
        "sublabel": "38/45 giường",
        "current": 38,
        "max": 45,
        "percent": 84.4,
        "colorThresholds": [
          { "from": 0,  "to": 70,  "color": "success" },
          { "from": 70, "to": 90,  "color": "warning" },
          { "from": 90, "to": 101, "color": "danger"  }
        ],
        "badge": "Đông",
        "badgeVariant": "warning"
      },
      {
        "id": "dept_icu",
        "label": "ICU",
        "sublabel": "18/20 giường",
        "current": 18,
        "max": 20,
        "percent": 90.0,
        "colorThresholds": [
          { "from": 0,  "to": 70,  "color": "success" },
          { "from": 70, "to": 90,  "color": "warning" },
          { "from": 90, "to": 101, "color": "danger"  }
        ],
        "badge": "Gần đầy",
        "badgeVariant": "danger"
      }
    ],
    "showPercent": true,
    "showValues": true
  }
}
```

| Field | Type | Description |
|---|---|---|
| `rows[].current` | number | Filled amount |
| `rows[].max` | number | Capacity |
| `rows[].percent` | number | Pre-computed `current/max * 100` |
| `rows[].colorThresholds` | array | Active color is the threshold whose `from ≤ percent < to` |
| `rows[].badge` | string\|null | Optional label chip on the right |
| `rows[].badgeVariant` | string\|null | Chip color variant |
| `showPercent` | boolean | Whether to render `"84%"` text |
| `showValues` | boolean | Whether to render `"38/45"` text |

**Compatible group**: `progress_rows` ↔ horizontal `bar_chart` (§7.1).

---

### 11.3 `flow_steps`

A horizontal or vertical step indicator showing process stages. Use for patient care pathway, OR scheduling, admission workflow.

```json
{
  "data": {
    "direction": "horizontal",
    "steps": [
      { "id": "register", "label": "Tiếp nhận",  "sublabel": "08:30", "status": "done",    "count": null },
      { "id": "triage",   "label": "Phân loại",  "sublabel": "08:45", "status": "done",    "count": null },
      { "id": "consult",  "label": "Khám bệnh",  "sublabel": "09:10", "status": "current", "count": 5    },
      { "id": "lab",      "label": "Xét nghiệm", "sublabel": null,    "status": "pending", "count": null },
      { "id": "pharmacy", "label": "Lấy thuốc",  "sublabel": null,    "status": "pending", "count": null },
      { "id": "discharge","label": "Ra viện",     "sublabel": null,    "status": "pending", "count": null }
    ]
  }
}
```

| Field | Type | Description |
|---|---|---|
| `direction` | `"horizontal"\|"vertical"` | Layout axis |
| `steps[].status` | `"done"\|"current"\|"warning"\|"error"\|"pending"` | Visual state of step |
| `steps[].count` | number\|null | Optional patient/item count badge |
| `steps[].sublabel` | string\|null | Secondary text (e.g. timestamp) |

**Edge cases:** single step renders without connector lines; `status: "error"` shows red X icon.

---

### 11.4 `timeline_vertical`

A vertical timeline of timestamped events. Use for patient history, incident log, audit trail.

```json
{
  "data": {
    "items": [
      {
        "id": "evt1",
        "timeLabel": "08:30",
        "isoTime": "2026-05-27T08:30:00Z",
        "title": "Nhập viện cấp cứu",
        "subtitle": "Khoa Cấp cứu • Giường C-03",
        "status": "done",
        "actor": "Y tá Nguyễn Thị A",
        "note": null
      },
      {
        "id": "evt2",
        "timeLabel": "09:15",
        "isoTime": "2026-05-27T09:15:00Z",
        "title": "Phẫu thuật nội soi",
        "subtitle": "Phòng mổ 02",
        "status": "current",
        "actor": "BS. Trần Văn B",
        "note": "Estimated 3h"
      },
      {
        "id": "evt3",
        "timeLabel": "12:00",
        "isoTime": "2026-05-27T12:00:00Z",
        "title": "Hồi sức sau mổ",
        "subtitle": "Phòng PACU",
        "status": "pending",
        "actor": null,
        "note": null
      }
    ],
    "showTime": true
  }
}
```

| Field | Type | Description |
|---|---|---|
| `items[].status` | `"done"\|"current"\|"pending"\|"skipped"\|"error"` | Icon and color state |
| `items[].actor` | string\|null | Who performed the action |
| `items[].note` | string\|null | Additional context |
| `showTime` | boolean | Whether to render `timeLabel` column |

---

### 11.5 `alert_list`

A real-time alert feed with severity levels. Use for clinical alerts, equipment alarms, SLA breaches.

```json
{
  "data": {
    "alerts": [
      {
        "id": "a-001",
        "level": 1,
        "title": "Tụt huyết áp nghiêm trọng",
        "subtitle": "BN: Nguyễn Văn A • ICU Giường 3",
        "time": "2026-05-27T08:45:00Z",
        "timeLabel": "08:45",
        "runbookId": "RB-HYPOTENSION-01",
        "acknowledged": false,
        "acknowledgedBy": null
      },
      {
        "id": "a-002",
        "level": 2,
        "title": "NEWS2 ≥ 5 — Theo dõi tăng cường",
        "subtitle": "BN: Trần Thị B • Nội phòng 201",
        "time": "2026-05-27T09:12:00Z",
        "timeLabel": "09:12",
        "runbookId": null,
        "acknowledged": false,
        "acknowledgedBy": null
      },
      {
        "id": "a-003",
        "level": 3,
        "title": "Thuốc sắp hết hạn",
        "subtitle": "Paracetamol 500mg • Kho dược B",
        "time": "2026-05-27T09:30:00Z",
        "timeLabel": "09:30",
        "runbookId": null,
        "acknowledged": true,
        "acknowledgedBy": "DS. Lê C"
      }
    ],
    "totalUnacknowledged": 2,
    "maxDisplay": 20
  }
}
```

| Field | Type | Description |
|---|---|---|
| `alerts[].level` | `1\|2\|3` | 1 = Critical (red), 2 = Urgent (amber), 3 = Advisory (blue) |
| `alerts[].runbookId` | string\|null | If present, frontend shows "View runbook" link |
| `alerts[].acknowledged` | boolean | Acknowledged alerts show dimmed / checked state |
| `totalUnacknowledged` | number | Badge count for parent nav item |
| `maxDisplay` | number | Max items to render; backend truncates before sending |

**Interaction**: `onClickDataPoint` on alert → opens drill-down with `clicked.id`.

---

### 11.6 `bed_grid`

Department-level bed availability grid. Each bed is a colored cell. Use for hospital-wide bed management.

```json
{
  "data": {
    "departments": [
      {
        "id": "icu",
        "name": "ICU",
        "floor": "2F",
        "beds": [
          { "id": "icu-01", "label": "01", "status": "occupied",  "patientId": "P-12345", "patientName": "Nguyễn A", "admittedAt": "2026-05-26T14:00:00Z" },
          { "id": "icu-02", "label": "02", "status": "available", "patientId": null,       "patientName": null,       "admittedAt": null },
          { "id": "icu-03", "label": "03", "status": "cleaning",  "patientId": null,       "patientName": null,       "admittedAt": null },
          { "id": "icu-04", "label": "04", "status": "reserved",  "patientId": null,       "patientName": "Trần B (pending)", "admittedAt": null },
          { "id": "icu-05", "label": "05", "status": "blocked",   "patientId": null,       "patientName": null,       "admittedAt": null }
        ],
        "summary": { "occupied": 1, "available": 1, "cleaning": 1, "reserved": 1, "blocked": 1, "total": 5 }
      }
    ],
    "legend": [
      { "status": "occupied",  "label": "Đang dùng",    "color": "danger"  },
      { "status": "available", "label": "Trống",         "color": "success" },
      { "status": "cleaning",  "label": "Đang dọn",     "color": "warning" },
      { "status": "reserved",  "label": "Đã đặt",       "color": "info"    },
      { "status": "blocked",   "label": "Không dùng",   "color": "neutral" }
    ]
  }
}
```

| Bed status | Color | Meaning |
|---|---|---|
| `occupied` | danger (red) | Patient admitted |
| `available` | success (green) | Ready for admission |
| `cleaning` | warning (amber) | Being cleaned post-discharge |
| `reserved` | info (blue) | Booked for incoming patient |
| `blocked` | neutral (gray) | Out of service / maintenance |

**Interaction**: `onClickDataPoint` on bed cell → `clicked.id` = bed ID, `clicked.patientId` = patient ID.

---

### 11.7 `room_status_grid`

Status grid for high-value rooms (OR, ICU pods, procedure rooms). Shows richer state than `bed_grid`.

```json
{
  "data": {
    "rooms": [
      {
        "id": "or-01",
        "label": "Phòng mổ 01",
        "status": "occupied",
        "primaryText": "Phẫu thuật tim hở",
        "secondaryText": "BS. Nguyễn A • Còn ~2h",
        "progressPercent": 65,
        "startTime": "2026-05-27T07:00:00Z",
        "estimatedEnd": "2026-05-27T11:00:00Z",
        "badgeLabel": "4h",
        "badgeVariant": "warning"
      },
      {
        "id": "or-02",
        "label": "Phòng mổ 02",
        "status": "available",
        "primaryText": "Sẵn sàng",
        "secondaryText": null,
        "progressPercent": null,
        "startTime": null,
        "estimatedEnd": null,
        "badgeLabel": null,
        "badgeVariant": null
      },
      {
        "id": "or-03",
        "label": "Phòng mổ 03",
        "status": "cleaning",
        "primaryText": "Đang vệ sinh",
        "secondaryText": "Hoàn thành lúc 10:30",
        "progressPercent": 40,
        "startTime": null,
        "estimatedEnd": null,
        "badgeLabel": null,
        "badgeVariant": null
      }
    ],
    "statusValues": ["available", "occupied", "cleaning", "reserved", "blocked", "emergency"]
  }
}
```

| Field | Type | Description |
|---|---|---|
| `status` | string | One of `statusValues` |
| `primaryText` | string | Main room state description |
| `secondaryText` | string\|null | Surgeon, time remaining, next procedure |
| `progressPercent` | number\|null | Operation progress 0–100; null = no progress bar |
| `badgeLabel` | string\|null | Duration or countdown label |

---

### 11.8 `map_pins`

Absolute-position pins on a background image (floor plan, city map, campus map). Use for ambulance tracking, asset location, department heatmap.

```json
{
  "data": {
    "backgroundUrl": "/assets/hospital-floor-2f.png",
    "backgroundType": "floor_plan",
    "width": 1000,
    "height": 600,
    "pins": [
      {
        "id": "vehicle-01",
        "x": 23.5,
        "y": 41.2,
        "label": "Xe cấp cứu 01",
        "sublabel": "Trên đường đến",
        "status": "active",
        "type": "ambulance",
        "metadata": { "eta": "8 phút", "patientName": "Nguyễn A", "origin": "120 Lê Lợi" }
      },
      {
        "id": "dept-icu",
        "x": 55.0,
        "y": 30.0,
        "label": "ICU",
        "sublabel": "18/20 giường",
        "status": "warning",
        "type": "department",
        "metadata": { "occupied": 18, "total": 20 }
      }
    ]
  }
}
```

| Field | Type | Description |
|---|---|---|
| `x`, `y` | number | Percentage of image width/height (0–100) |
| `status` | `"active"\|"warning"\|"danger"\|"idle"\|"offline"` | Pin color state |
| `type` | string | Icon hint: `"ambulance"`, `"department"`, `"asset"`, `"patient"`, `"custom"` |
| `metadata` | object\|null | Arbitrary key-value for tooltip |

**Interaction**: `onClickDataPoint` on pin → `clicked.id` = pin ID, `clicked.metadata` = full metadata object.

---

### 11.9 `patient_flow_stages`

Patient count at each stage of care. Use for ER flow visualization, bed throughput, discharge planning.

```json
{
  "data": {
    "stages": [
      { "id": "waiting",    "label": "Chờ khám",       "count": 47, "avgWaitMin": 32, "status": "warning", "icon": "clock"  },
      { "id": "triaged",    "label": "Đã phân loại",   "count": 12, "avgWaitMin": 8,  "status": "ok",      "icon": null     },
      { "id": "in_consult", "label": "Đang khám",      "count": 23, "avgWaitMin": 15, "status": "ok",      "icon": null     },
      { "id": "lab",        "label": "Xét nghiệm",     "count": 18, "avgWaitMin": 45, "status": "warning", "icon": "clock"  },
      { "id": "discharge",  "label": "Chờ xuất viện",  "count": 9,  "avgWaitMin": 20, "status": "ok",      "icon": null     }
    ],
    "totalPatients": 109,
    "thresholds": {
      "waitWarningMin": 30,
      "waitDangerMin": 60
    }
  }
}
```

| Field | Type | Description |
|---|---|---|
| `stages[].count` | number | Patients currently at this stage |
| `stages[].avgWaitMin` | number | Average time in this stage (minutes) |
| `stages[].status` | `"ok"\|"warning"\|"danger"` | Color state for this stage box |

**Compatible group**: `patient_flow_stages` ↔ `funnel` (§7.1). The `funnel` type shows drop rates between stages; `patient_flow_stages` shows live counts with wait times.

---

### 11.10 `risk_tiers`

4-tier risk stratification display. Use for population health management, readmission risk, sepsis screening.

```json
{
  "data": {
    "tiers": [
      { "level": 1, "label": "Nguy cơ rất cao", "count": 12,  "percent": 3.2,  "color": "danger",  "action": "Can thiệp ngay",       "changeFromPrev": +2  },
      { "level": 2, "label": "Nguy cơ cao",     "count": 45,  "percent": 12.0, "color": "warning", "action": "Theo dõi chặt",         "changeFromPrev": -3  },
      { "level": 3, "label": "Nguy cơ TB",      "count": 124, "percent": 33.1, "color": "info",    "action": "Tái khám định kỳ",     "changeFromPrev": +8  },
      { "level": 4, "label": "Nguy cơ thấp",   "count": 194, "percent": 51.7, "color": "success", "action": "Duy trì điều trị",     "changeFromPrev": -7  }
    ],
    "total": 375,
    "calculatedAt": "2026-05-27T06:00:00Z",
    "modelVersion": "sepsis-risk-v2.1"
  }
}
```

| Field | Type | Description |
|---|---|---|
| `tiers[].level` | `1\|2\|3\|4` | 1 = highest risk |
| `tiers[].action` | string | Recommended clinical action label |
| `tiers[].changeFromPrev` | number\|null | Change in patient count vs previous calculation |
| `modelVersion` | string\|null | AI/ML model that produced this stratification |

---

### 11.11 `news2_bars`

Per-patient NEWS2 (National Early Warning Score 2) bar display. Use for ward overview, deterioration monitoring.

```json
{
  "data": {
    "patients": [
      {
        "id": "P-12345",
        "name": "Nguyễn Văn A",
        "ward": "ICU",
        "bed": "03",
        "score": 7,
        "level": "L3",
        "trend": "up",
        "components": {
          "respRate":      2,
          "spO2":          0,
          "airO2":         2,
          "bp":            1,
          "heartRate":     1,
          "consciousness": 0,
          "temp":          1
        },
        "lastAssessed": "2026-05-27T09:00:00Z",
        "alertSent": true
      },
      {
        "id": "P-67890",
        "name": "Trần Thị B",
        "ward": "Nội",
        "bed": "201A",
        "score": 3,
        "level": "L1",
        "trend": "stable",
        "components": {
          "respRate": 0, "spO2": 0, "airO2": 0, "bp": 1, "heartRate": 2, "consciousness": 0, "temp": 0
        },
        "lastAssessed": "2026-05-27T08:00:00Z",
        "alertSent": false
      }
    ],
    "thresholds": [
      { "label": "L1 Routine",  "scoreFrom": 0, "scoreTo": 4,  "color": "success" },
      { "label": "L2 Tăng TĐ", "scoreFrom": 5, "scoreTo": 6,  "color": "warning" },
      { "label": "L3 Khẩn",    "scoreFrom": 7, "scoreTo": 20, "color": "danger"  }
    ],
    "maxScore": 20
  }
}
```

| Field | Type | Description |
|---|---|---|
| `patients[].score` | 0–20 | Total NEWS2 score |
| `patients[].level` | `"L1"\|"L2"\|"L3"` | Derived from thresholds |
| `patients[].trend` | `"up"\|"down"\|"stable"` | Change vs previous assessment |
| `patients[].components` | object | Individual NEWS2 sub-scores (0–3 each) |
| `patients[].alertSent` | boolean | Whether an escalation alert was dispatched |

**Interaction**: `onClickDataPoint` → `clicked.id` = patient ID.

---

### 11.12 `chat_panel`

An embedded AI chatbot widget. Use for medical AI assistant, drug interaction checker, clinical decision support.

```json
{
  "data": {
    "systemRole": "Bạn là trợ lý AI y tế của HDOS. Trả lời ngắn gọn, chính xác dựa trên dữ liệu bệnh viện hiện tại. Không tự chẩn đoán bệnh.",
    "operationPattern": "ai.chat.medical",
    "model": "gpt-4o",
    "quickQuestions": [
      "Bệnh nhân nguy hiểm nhất hiện tại?",
      "Tình trạng giường ICU hôm nay?",
      "Phòng mổ nào đang trống?"
    ],
    "allowedRoles": ["doctor", "nurse", "admin"],
    "welcomeMessage": "Xin chào! Tôi có thể trả lời câu hỏi về dữ liệu bệnh viện.",
    "inputPlaceholder": "Hỏi về bệnh nhân, giường, lịch mổ...",
    "maxHistoryItems": 20,
    "contextParams": {
      "tenantId": "{{user.tenantId}}",
      "ward": "{{filters.ward}}",
      "date": "{{filters.date}}"
    }
  }
}
```

| Field | Type | Description |
|---|---|---|
| `operationPattern` | string | HDOS operation that handles chat messages |
| `model` | string\|null | AI model hint (informational; backend decides actual model) |
| `quickQuestions` | string[] | Pre-defined question chips shown above input |
| `allowedRoles` | string[] | Frontend hides widget if user role not in list |
| `contextParams` | object | Template params injected into each chat operation call. Uses same `{{token}}` syntax as §6.3 |
| `maxHistoryItems` | number | Max turns to keep in local history (oldest dropped) |

**Chat operation contract**: The `operationPattern` operation receives:
```json
{
  "message": "user message text",
  "history": [
    { "role": "user",      "content": "..." },
    { "role": "assistant", "content": "..." }
  ],
  "contextParams": { "tenantId": "...", "ward": "...", "date": "..." }
}
```
And returns `Terminal.payloadJson`:
```json
{
  "reply": "Hiện tại ICU còn 2 giường trống: giường 04 và 05.",
  "sources": [
    { "label": "Dữ liệu giường ICU", "url": null }
  ],
  "suggestedQuestions": ["Chi tiết giường 04?", "Lịch sử bệnh nhân ICU 03?"]
}
```

---

## 12. JSON Schema Registry

Every chart type has a versioned JSON Schema. Retrieve via:

```http
GET /api/v1/admin/schemas?type=render&chartType=advanced_table
GET /api/v1/admin/schemas?type=render&chartType=kpi_grid
GET /api/v1/admin/schemas?type=render&chartType=bed_grid
```

Schema IDs follow: `https://platform.example.com/schemas/render/v1/{chartType}`

Use during development for runtime payload validation.

**Registered chart types** (28 total):

| # | chartType | Category |
|---|---|---|
| 1 | `line_chart` | Visualization |
| 2 | `bar_chart` | Visualization |
| 3 | `area_chart` | Visualization |
| 4 | `pie_chart` | Visualization |
| 5 | `donut_chart` | Visualization |
| 6 | `kpi` | Visualization |
| 7 | `gauge` | Visualization |
| 8 | `heatmap` | Visualization |
| 9 | `scatter` | Visualization |
| 10 | `advanced_table` | Visualization |
| 11 | `simple_table` | Visualization |
| 12 | `pivot_table` | Visualization |
| 13 | `funnel` | Visualization |
| 14 | `filter_dropdown` | Filter |
| 15 | `filter_date_range` | Filter |
| 16 | `filter_slider` | Filter |
| 17 | `filter_search` | Filter |
| 18 | `text_widget` | Layout |
| 19 | `tab_container` | Layout |
| 20 | `kpi_grid` | Healthcare |
| 21 | `progress_rows` | Healthcare |
| 22 | `flow_steps` | Healthcare |
| 23 | `timeline_vertical` | Healthcare |
| 24 | `alert_list` | Healthcare |
| 25 | `bed_grid` | Healthcare |
| 26 | `room_status_grid` | Healthcare |
| 27 | `map_pins` | Healthcare |
| 28 | `patient_flow_stages` | Healthcare |
| 29 | `risk_tiers` | Healthcare |
| 30 | `news2_bars` | Healthcare |
| 31 | `chat_panel` | Healthcare/AI |

> Note: total is 31 unique `chartType` values (some filter/layout types share the same data category). The "28 contracts" count in the header refers to distinct rendering contracts; `filter_*` types share a common envelope.

**CI enforcement:** every transformer in `Shared/Transformers/` has a golden file at `tests/Transformers.Tests/golden/{chartType}.json`. A change to a transformer's output MUST update both the golden file and `RENDER_CONTRACTS.md` in the same commit. CI fails if they diverge.

---

## 13. Changelog

- **v7.0 (2026-05-27)**: Added §11 — 12 healthcare & clinical widget types: `kpi_grid`, `progress_rows`, `flow_steps`, `timeline_vertical`, `alert_list`, `bed_grid`, `room_status_grid`, `map_pins`, `patient_flow_stages`, `risk_tiers`, `news2_bars`, `chat_panel`. Extended §7.1 compatible groups (progress_rows ↔ bar_chart horizontal; patient_flow_stages ↔ funnel; kpi_grid ↔ kpi). Added §10.3 `resultChartType` field in operation registry. Updated §12 schema registry table to 28 types. Provider teams: read §10.3 and §11 to understand which `resultChartType` to declare.
- **v6.2 (2026-05-18)**: Complete rewrite. Added all 16 widget types with full JSON schemas and examples. Added: server-side table protocol (§8), 7 computed transforms (§9), drill-down interactions (§6), chart type switching (§7), filter widget interaction protocol (§4.5). Replaced flat chart type list with 3 category structure. Golden file CI enforcement.
- **v6.1 (2026-05-18)**: Initial version — 14 internal chart types + external provider chart types + transformer outputs.
