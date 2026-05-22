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

// Mirrors backend ResponseDispatchPushMessage (MessagePack [Key] names).
// Pushed over SignalR on terminal state — carries the full result so the client
// does not need to GET /requests/{id}/result.
export interface RequestCompletedEvent {
  requestId: string;
  // wire values: "done" | "failed" | "timeout" | "cancelled"
  status: string;
  operation: string;
  payloadJson?: string | null;
  error?: { code?: string; message?: string } | null;
  elapsedMs?: number;
}

export interface RequestFailedEvent {
  requestId: string;
  status: string;
  operation: string;
  payloadJson?: string | null;
  error?: { code?: string; message?: string } | null;
  elapsedMs?: number;
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

// ─── New report response shapes ───────────────────────────────────────────────

export interface ChannelComparisonResponse {
  online: { revenue: number; units: number; percentage: number };
  store: { revenue: number; units: number; percentage: number };
  trend: { labels: string[]; online: number[]; store: number[] };
}

export interface ProductDetailResponse {
  productName: string;
  totalRevenue: number;
  totalUnits: number;
  avgDailyRevenue: number;
  byRegion: { name: string; revenue: number; units: number }[];
  trend: { labels: string[]; revenue: number[]; units: number[] };
}

export interface TopPerformersResponse {
  topProducts: { rank: number; name: string; revenue: number; growth: number }[];
  topRegions: { rank: number; name: string; revenue: number; growth: number }[];
  period: string;
}

// ─── Report type registry ─────────────────────────────────────────────────────

export type ReportOperation =
  | 'report.dashboard.summary'
  | 'report.sales.trend'
  | 'report.inventory.status'
  | 'report.regional.performance'
  | 'report.channel.comparison'
  | 'report.product.detail'
  | 'report.top.performers';

export interface ReportTypeDefinition {
  operation: ReportOperation;
  label: string;
  description: string;
}

export const REPORT_TYPES: ReportTypeDefinition[] = [
  {
    operation: 'report.dashboard.summary',
    label: 'Tổng quan Dashboard',
    description: 'KPI ngày hôm nay: doanh thu, số lượng và cảnh báo',
  },
  {
    operation: 'report.sales.trend',
    label: 'Xu hướng bán hàng',
    description: 'Xu hướng bán hàng theo khoảng thời gian, nhóm theo ngày/tuần/tháng',
  },
  {
    operation: 'report.inventory.status',
    label: 'Tình trạng tồn kho',
    description: 'Mức tồn kho hiện tại và trạng thái tất cả sản phẩm',
  },
  {
    operation: 'report.regional.performance',
    label: 'Hiệu suất theo khu vực',
    description: 'Doanh thu và mức đạt theo khu vực cho kỳ đã chọn',
  },
  {
    operation: 'report.channel.comparison',
    label: 'So sánh kênh bán hàng',
    description: 'Phân tích đóng góp và xu hướng của kênh Online vs Cửa hàng',
  },
  {
    operation: 'report.product.detail',
    label: 'Chi tiết sản phẩm',
    description: 'Phân tích doanh thu và xu hướng chi tiết cho từng sản phẩm',
  },
  {
    operation: 'report.top.performers',
    label: 'Top hiệu suất',
    description: 'Bảng xếp hạng top 5 sản phẩm và khu vực tốt nhất',
  },
];
