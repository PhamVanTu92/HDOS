// ─── Request / Response contracts ────────────────────────────────────────────

export type Priority = 'Low' | 'Normal' | 'High';

export interface RequestOptions {
  priority: Priority;
  cacheSeconds?: number;
  timeoutMs?: number;
}

export interface RequestEnvelope {
  requestId: string;
  operation: string;
  params: Record<string, unknown>;
  tenantId: string;
  userId: string;
  options: RequestOptions;
}

export interface SubmitAck {
  requestId: string;
  queuedAt: string;
}

export type RequestStatus =
  | 'Queued'
  | 'Processing'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

export interface RequestResult<T = unknown> {
  requestId: string;
  status: RequestStatus;
  operation: string;
  completedAt?: string;
  data?: T;
  error?: string;
}

// ─── SignalR event payloads ───────────────────────────────────────────────────

export interface RequestCompletedEvent {
  requestId: string;
  operation: string;
  tenantId: string;
  userId: string;
}

export interface RequestFailedEvent {
  requestId: string;
  error: string;
}

export interface RequestCancelledEvent {
  requestId: string;
}

export interface WidgetStaleEvent {
  channel: string;
  widgetId: string;
  operation: string;
  params: Record<string, unknown>;
}

// ─── Operation response shapes ────────────────────────────────────────────────

export interface DashboardSummary {
  totalRevenue: number;
  totalUnits: number;
  topRegion: string;
  topProduct: string;
  alerts: string[];
}

export interface SalesTrendSeries {
  name: string;
  data: number[];
}

export interface SalesTrend {
  labels: string[];
  series: SalesTrendSeries[];
}

export type GroupBy = 'day' | 'week' | 'month';

export interface SalesTrendParams {
  fromDate: string;
  toDate: string;
  groupBy: GroupBy;
}

export type StockStatus = 'ok' | 'low' | 'out';

export interface InventoryProduct {
  name: string;
  category: string;
  stock: number;
  status: StockStatus;
}

export interface InventoryStatus {
  products: InventoryProduct[];
}

export type Period = 'today' | 'week' | 'month';

export interface RegionPerformanceRow {
  name: string;
  revenue: number;
  units: number;
  target: number;
  achievementPct: number;
}

export interface RegionalPerformance {
  regions: RegionPerformanceRow[];
}

// ─── Report type registry ─────────────────────────────────────────────────────

export type ReportOperation =
  | 'report.dashboard.summary'
  | 'report.sales.trend'
  | 'report.inventory.status'
  | 'report.regional.performance';

export interface ReportTypeDefinition {
  operation: ReportOperation;
  label: string;
  description: string;
}

export const REPORT_TYPES: ReportTypeDefinition[] = [
  {
    operation: 'report.dashboard.summary',
    label: 'Dashboard Summary',
    description: 'KPIs for today including revenue, units, and alerts',
  },
  {
    operation: 'report.sales.trend',
    label: 'Sales Trend',
    description: 'Sales trend over a date range grouped by day/week/month',
  },
  {
    operation: 'report.inventory.status',
    label: 'Inventory Status',
    description: 'Current inventory levels and stock status for all products',
  },
  {
    operation: 'report.regional.performance',
    label: 'Regional Performance',
    description: 'Revenue and achievement by region for a given period',
  },
];
