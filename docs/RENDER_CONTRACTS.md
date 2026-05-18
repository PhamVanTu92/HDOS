# RENDER_CONTRACTS.md — Widget Render Payload Contracts
> Version: 6.2 | Audience: Frontend team | Last updated: 2026-05-18

This document is the **frontend rendering contract**. Frontend reads `chartType` from each widget payload and renders accordingly. Backend NEVER sends business-specific shapes — everything conforms to one of the 16 contracts below.

**Rule**: if `chartType` is unknown, render `raw_json` fallback (§7). Never crash.

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

11. [JSON Schema Registry](#11-json-schema-registry)

12. [Changelog](#12-changelog)

---

## 1. Common Envelope

### 1.1 `meta` block — present in every widget payload

```json
{
  "meta": {
    "renderContractVersion": "1.0",
    "generatedAt": "2026-05-18T10:00:00Z",
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
| `data` | Chart-type-specific payload — see §3–5 |
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
  "renderedAt": "2026-05-18T10:00:00Z",
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

---

## 11. JSON Schema Registry

Every chart type has a versioned JSON Schema. Retrieve via:

```http
GET /api/v1/admin/schemas?type=render&chartType=advanced_table
```

Schema IDs follow: `https://platform.example.com/schemas/render/v1/{chartType}`

Use during development for runtime payload validation.

**CI enforcement:** every transformer in `Shared/Transformers/` has a golden file at `tests/Transformers.Tests/golden/{chartType}.json`. A change to a transformer's output MUST update both the golden file and `RENDER_CONTRACTS.md` in the same commit. CI fails if they diverge.

---

## 12. Changelog

- **v6.2 (2026-05-18)**: Complete rewrite. Added all 16 widget types with full JSON schemas and examples. Added: server-side table protocol (§8), 7 computed transforms (§9), drill-down interactions (§6), chart type switching (§7), filter widget interaction protocol (§4.5). Replaced flat chart type list with 3 category structure. Golden file CI enforcement.
- **v6.1 (2026-05-18)**: Initial version — 14 internal chart types + external provider chart types + transformer outputs.
