// Module system types — matches API responses from ModuleController
// GET /api/v1/modules            → SidebarGroup[]
// GET /api/v1/modules/:slug/layout → ModuleLayout

// ── Sidebar ───────────────────────────────────────────────────────────────────

export interface SidebarModule {
  id:        string;
  slug:      string;
  label:     string;
  icon:      string | null;
  sortOrder: number;
}

export interface SidebarGroup {
  id:        string;
  slug:      string;
  label:     string;
  icon:      string | null;
  sortOrder: number;
  modules:   SidebarModule[];
}

// ── Module Layout ─────────────────────────────────────────────────────────────

export interface WidgetLayout {
  widgetKey:        string;
  title:            string | null;
  subtitle:         string | null;
  chartType:        WidgetChartType;
  gridX:            number;
  gridY:            number;
  gridW:            number;
  gridH:            number;
  operationPattern: string | null;
  providerId:       string | null;
  /** JSON string — template params with {{token}} placeholders */
  paramsTemplate:   string;
  /** JSON string — visual config passthrough */
  visualConfig:     string;
  filterBindings:   string[];
  /** JSON string — interactions config */
  interactions:     string;
  filterKey:        string | null;
}

export interface ModuleTab {
  id:        string;
  slug:      string;
  label:     string;
  sortOrder: number;
  isDefault: boolean;
  widgets:   WidgetLayout[];
}

export interface ModuleLayout {
  slug:                   string;
  refreshIntervalSeconds: number | null;
  tabs:                   ModuleTab[];
}

// ── Widget Type Catalog ───────────────────────────────────────────────────────

export type WidgetChartType =
  // Visualization
  | 'line_chart' | 'bar_chart' | 'area_chart'
  | 'pie_chart'  | 'donut_chart'
  | 'kpi'        | 'gauge'
  | 'heatmap'    | 'scatter'
  | 'advanced_table' | 'simple_table' | 'pivot_table'
  | 'funnel'
  // Filter
  | 'filter_dropdown' | 'filter_date_range' | 'filter_slider' | 'filter_search'
  // Layout
  | 'text_widget' | 'tab_container'
  // Healthcare
  | 'kpi_grid'        | 'progress_rows'     | 'flow_steps'
  | 'timeline_vertical' | 'alert_list'      | 'bed_grid'
  | 'room_status_grid' | 'map_pins'         | 'patient_flow_stages'
  | 'risk_tiers'      | 'news2_bars'
  // AI
  | 'chat_panel'
  // Fallback
  | 'raw_json'
  | (string & {}); // allow unknown types without losing type safety

export type WidgetCategory =
  | 'visualization' | 'filter' | 'layout' | 'healthcare' | 'ai';

export interface WidgetTypeCatalogEntry {
  chartType:       WidgetChartType;
  category:        WidgetCategory;
  label:           string;
  description:     string | null;
  icon:            string | null;
  rowSchema:       string;    // JSON Schema string
  requiredColumns: string[];
  optionalColumns: string[];
  compatibleWith:  WidgetChartType[];
  sortOrder:       number;
}

// ── Compatible groups (mirrors RENDER_CONTRACTS.md §7.1) ─────────────────────

export const COMPATIBLE_GROUPS: WidgetChartType[][] = [
  ['line_chart', 'bar_chart', 'area_chart'],
  ['pie_chart', 'donut_chart'],
  ['simple_table', 'advanced_table'],
  ['progress_rows'],  // compatible with horizontal bar_chart — handled at render time
  ['patient_flow_stages', 'funnel'],
  ['kpi_grid', 'kpi'],
];

export function getCompatibleTypes(chartType: WidgetChartType): WidgetChartType[] {
  const group = COMPATIBLE_GROUPS.find(g => g.includes(chartType as string));
  return group ?? [chartType];
}

// ── Admin Module DTOs ─────────────────────────────────────────────────────────

export interface AdminModule {
  id:                    string;
  slug:                  string;
  label:                 string;
  icon:                  string | null;
  description:           string | null;
  requiredRoles:         string[] | null;
  sortOrder:             number;
  isVisible:             boolean;
  isActive:              boolean;
  refreshIntervalSeconds: number | null;
  createdAt:             string;
  groupSlug:             string;
  groupLabel:            string;
}

export interface UpsertModuleRequest {
  groupId:               string;
  slug:                  string;
  label:                 string;
  icon?:                 string;
  description?:          string;
  requiredRoles?:        string[];
  sortOrder?:            number;
  isVisible?:            boolean;
  isActive?:             boolean;
  refreshIntervalSeconds?: number;
}

export interface UpsertWidgetRequest {
  widgetKey:       string;
  title?:          string;
  subtitle?:       string;
  chartType:       WidgetChartType;
  gridX:           number;
  gridY:           number;
  gridW:           number;
  gridH:           number;
  operationPattern?: string;
  providerId?:     string;
  paramsTemplate?: string;
  visualConfig?:   string;
  filterBindings?: string[];
  interactions?:   string;
  filterKey?:      string;
}
