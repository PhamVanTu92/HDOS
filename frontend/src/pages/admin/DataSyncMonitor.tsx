import { useState, useCallback, useRef, useEffect } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { useSignalREvent, useWidgetSubscription } from '../../hooks/useSignalR';
import { submitRequest, getRequestResult, mapPushToResult } from '../../api/requests';
import { hasRealmRole } from '../../api/client';
import { sseClient } from '../../api/sse';
import type {
  RequestCompletedEvent,
  RequestFailedEvent,
  WidgetStaleEvent,
  DashboardSummary,
  RegionalPerformance,
  RegionPerformanceRow,
  RequestResult,
} from '../../types/contracts';

const WIDGET_CHANNEL = 'widget:main-dashboard:main-dashboard';

// ── Types ─────────────────────────────────────────────────────────────────────

type EventKind = 'startup' | 'push' | 'refreshing' | 'done' | 'error';

interface TimelineEntry {
  id: string;
  ts: Date;
  kind: EventKind;
  headline: string;
  detail?: string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(n: number) {
  return new Intl.NumberFormat('vi-VN').format(n);
}

function fmtTime(d: Date) {
  return d.toLocaleTimeString('vi-VN', { hour12: false });
}

function fmtElapsed(ms: number) {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`;
}

function numDelta(next: number, prev: number | undefined) {
  if (prev === undefined || next === prev) return null;
  return next > prev ? 'up' : 'down';
}

// EventSource readyState labels for diagnostics
function sseStateLabel(es: EventSource | null): { label: string; color: string } {
  if (!es) return { label: 'Chưa khởi tạo', color: 'text-gray-400' };
  switch (es.readyState) {
    case EventSource.CONNECTING: return { label: 'Đang kết nối…', color: 'text-amber-500' };
    case EventSource.OPEN:       return { label: 'Đã kết nối ✓',  color: 'text-green-600' };
    case EventSource.CLOSED:     return { label: 'Đã đóng ✗',     color: 'text-red-500'   };
    default:                     return { label: 'Không rõ',       color: 'text-gray-400'  };
  }
}

// ── Icon components ───────────────────────────────────────────────────────────

function SignalIcon({ open }: { open: boolean }) {
  return (
    <svg className={`h-4 w-4 ${open ? 'text-green-500' : 'text-amber-400'}`}
      fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0" />
    </svg>
  );
}

function RefreshIcon({ spin }: { spin?: boolean }) {
  return (
    <svg className={`h-4 w-4 ${spin ? 'animate-spin' : ''}`}
      fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
    </svg>
  );
}

function ArrowUpIcon() {
  return <svg className="inline h-3.5 w-3.5 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 15l7-7 7 7" />
  </svg>;
}

function ArrowDownIcon() {
  return <svg className="inline h-3.5 w-3.5 text-red-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M19 9l-7 7-7-7" />
  </svg>;
}

// ── Timeline badge ────────────────────────────────────────────────────────────

function KindBadge({ kind }: { kind: EventKind }) {
  const cfg: Record<EventKind, { dot: string; label: string }> = {
    startup:    { dot: 'bg-gray-400',                      label: 'Khởi động'   },
    push:       { dot: 'bg-indigo-500',                    label: 'Excel push'  },
    refreshing: { dot: 'bg-amber-400 animate-pulse',       label: 'Làm mới…'   },
    done:       { dot: 'bg-green-500',                     label: 'Đã cập nhật' },
    error:      { dot: 'bg-red-500',                       label: 'Lỗi'         },
  };
  const { dot, label } = cfg[kind];
  return (
    <span className="flex items-center gap-1.5">
      <span className={`inline-block h-2.5 w-2.5 rounded-full shrink-0 ${dot}`} />
      <span className="text-xs font-medium text-gray-700">{label}</span>
    </span>
  );
}

// ── KPI card ──────────────────────────────────────────────────────────────────

function KpiCard({
  label, value, prev, unit = '',
}: {
  label: string;
  value: number | string;
  prev?: number;
  unit?: string;
}) {
  const num = typeof value === 'number' ? value : undefined;
  const dir = num !== undefined ? numDelta(num, prev) : null;
  return (
    <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wider text-gray-400">{label}</p>
      <p className="mt-1.5 text-2xl font-bold text-gray-800">
        {num !== undefined ? fmt(num) : value}
        {unit && <span className="ml-1 text-sm font-normal text-gray-400">{unit}</span>}
        {dir === 'up'   && <span className="ml-2"><ArrowUpIcon /></span>}
        {dir === 'down' && <span className="ml-2"><ArrowDownIcon /></span>}
      </p>
    </div>
  );
}

// ── Regional row ──────────────────────────────────────────────────────────────

function RegionRow({ row, prev }: { row: RegionPerformanceRow; prev?: RegionPerformanceRow }) {
  const dir = prev ? numDelta(row.achievementPct, prev.achievementPct) : null;
  const pct = Math.min(100, Math.max(0, row.achievementPct));
  return (
    <tr className="border-b border-gray-100 last:border-0 hover:bg-gray-50 transition-colors">
      <td className="py-2.5 px-4 text-sm font-medium text-gray-800">{row.name}</td>
      <td className="py-2.5 px-4 text-sm text-gray-600 text-right tabular-nums">{fmt(row.revenue)}</td>
      <td className="py-2.5 px-4 text-sm text-gray-500 text-right tabular-nums">{fmt(row.units)}</td>
      <td className="py-2.5 px-4">
        <div className="flex items-center justify-end gap-2">
          <div className="h-1.5 w-20 rounded-full bg-gray-200">
            <div className={`h-1.5 rounded-full transition-all duration-500 ${
              pct >= 80 ? 'bg-green-500' : pct >= 60 ? 'bg-amber-400' : 'bg-red-400'
            }`} style={{ width: `${pct}%` }} />
          </div>
          <span className={`text-xs font-semibold w-10 text-right ${
            pct >= 80 ? 'text-green-600' : pct >= 60 ? 'text-amber-600' : 'text-red-500'
          }`}>{pct.toFixed(1)}%</span>
          {dir === 'up'   && <ArrowUpIcon />}
          {dir === 'down' && <ArrowDownIcon />}
        </div>
      </td>
    </tr>
  );
}

// ── Skeleton ──────────────────────────────────────────────────────────────────

function SkeletonCard() {
  return (
    <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm animate-pulse">
      <div className="h-3 w-20 rounded bg-gray-200 mb-3" />
      <div className="h-7 w-28 rounded bg-gray-200" />
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function DataSyncMonitor() {
  const auth     = useAuth();
  const userId   = auth.user?.profile.sub ?? '';
  const tenantId = (auth.user?.profile['tenant_id'] as string | undefined) ?? userId;

  if (!hasRealmRole('admin')) {
    return (
      <div className="flex items-center justify-center h-full">
        <p className="text-red-600">Không có quyền truy cập trang này.</p>
      </div>
    );
  }

  // ── Timeline ─────────────────────────────────────────────────────────────────

  const [timeline, setTimeline] = useState<TimelineEntry[]>([]);
  const addEntry = useCallback((kind: EventKind, headline: string, detail?: string) => {
    setTimeline(prev => [
      { id: crypto.randomUUID(), ts: new Date(), kind, headline, detail },
      ...prev.slice(0, 99),
    ]);
  }, []);

  // ── SSE connection state (poll every 2 s) ─────────────────────────────────────

  // Expose the internal EventSource for readyState diagnostics
  const [esState, setEsState] = useState<0 | 1 | 2 | null>(null);
  useEffect(() => {
    const tick = () => {
      const es = (sseClient as unknown as { eventSource: EventSource | null }).eventSource;
      setEsState(es ? (es.readyState as 0 | 1 | 2) : null);
    };
    tick();
    const id = setInterval(tick, 2000);
    return () => clearInterval(id);
  }, []);

  const sseOpen = esState === EventSource.OPEN;
  const { label: sseLabel, color: sseColor } = sseStateLabel(
    esState !== null ? { readyState: esState } as EventSource : null
  );

  // ── Operation state — summary ─────────────────────────────────────────────────

  const [summaryReqId, setSummaryReqId] = useState<string | null>(null);
  const [summaryPush,  setSummaryPush]  = useState<RequestResult<DashboardSummary> | null>(null);
  const [prevSummary,  setPrevSummary]  = useState<DashboardSummary | null>(null);

  const { data: summaryPolled } = useQuery<RequestResult<DashboardSummary>>({
    queryKey: ['sync-summary', summaryReqId],
    queryFn:  () => getRequestResult<DashboardSummary>(summaryReqId!),
    enabled:  !!summaryReqId && !summaryPush,
    retry: false,
    refetchInterval: (q) => {
      if (summaryPush || q.state.error) return false;
      const s = q.state.data?.status;
      return s && ['Completed', 'Failed', 'Cancelled'].includes(s) ? false : 3000;
    },
    staleTime: 0,
  });

  // ── Operation state — regions ─────────────────────────────────────────────────

  const [regionsReqId, setRegionsReqId] = useState<string | null>(null);
  const [regionsPush,  setRegionsPush]  = useState<RequestResult<RegionalPerformance> | null>(null);
  const [prevRegions,  setPrevRegions]  = useState<RegionPerformanceRow[]>([]);

  const { data: regionsPolled } = useQuery<RequestResult<RegionalPerformance>>({
    queryKey: ['sync-regions', regionsReqId],
    queryFn:  () => getRequestResult<RegionalPerformance>(regionsReqId!),
    enabled:  !!regionsReqId && !regionsPush,
    retry: false,
    refetchInterval: (q) => {
      if (regionsPush || q.state.error) return false;
      const s = q.state.data?.status;
      return s && ['Completed', 'Failed', 'Cancelled'].includes(s) ? false : 3000;
    },
    staleTime: 0,
  });

  // ── Resolved results (push wins over poll) ────────────────────────────────────

  const summaryResult = summaryPush ?? summaryPolled ?? null;
  const regionsResult = regionsPush ?? regionsPolled ?? null;

  const summary = summaryResult?.status === 'Completed' ? summaryResult.data ?? null : null;
  const regions = regionsResult?.status === 'Completed' ? (regionsResult.data?.regions ?? []) : [];

  // ── Misc state ────────────────────────────────────────────────────────────────

  const [pushCount,    setPushCount]    = useState(0);
  const [lastRefreshAt, setLastRefreshAt] = useState<Date | null>(null);

  // Timing map: requestId → startedAt  (for elapsed display)
  const startedAtRef = useRef(new Map<string, number>());
  // Guard: prevent double-submit
  const isRefreshingRef = useRef(false);
  // Track pending count to know when both ops complete
  const pendingCountRef = useRef(0);

  // isRefreshing: true while any result is still pending
  const [isRefreshing, setIsRefreshing] = useState(false);

  const markDoneIfComplete = useCallback(() => {
    pendingCountRef.current -= 1;
    if (pendingCountRef.current <= 0) {
      pendingCountRef.current  = 0;
      isRefreshingRef.current  = false;
      setIsRefreshing(false);
      setLastRefreshAt(new Date());
    }
  }, []);

  // ── Refresh ───────────────────────────────────────────────────────────────────

  const refresh = useCallback(async () => {
    if (isRefreshingRef.current) return;
    isRefreshingRef.current = true;
    pendingCountRef.current = 2;
    setIsRefreshing(true);
    setSummaryPush(null);
    setRegionsPush(null);

    try {
      const [ackS, ackR] = await Promise.all([
        submitRequest({ operation: 'report.dashboard.summary',     params: {},              tenantId, userId, priority: 'High' }),
        submitRequest({ operation: 'report.regional.performance',  params: { period: 'today' }, tenantId, userId, priority: 'High' }),
      ]);

      const now = Date.now();
      startedAtRef.current.set(ackS.requestId, now);
      startedAtRef.current.set(ackR.requestId, now);

      setSummaryReqId(ackS.requestId);
      setRegionsReqId(ackR.requestId);
      addEntry('refreshing', 'Đang lấy dữ liệu mới…',
        sseOpen ? 'Chờ SSE push' : 'SSE chưa kết nối — dùng HTTP polling (3s)');
    } catch (err) {
      isRefreshingRef.current = false;
      pendingCountRef.current = 0;
      setIsRefreshing(false);
      addEntry('error', 'Gửi yêu cầu thất bại', err instanceof Error ? err.message : String(err));
    }
  }, [tenantId, userId, addEntry, sseOpen]);

  // ── SSE handlers — RequestCompleted / RequestFailed ───────────────────────────

  useSignalREvent('RequestCompleted', (payload: RequestCompletedEvent) => {
    const started = startedAtRef.current.get(payload.requestId);
    const result  = mapPushToResult(payload);

    if (payload.requestId === summaryReqId) {
      setSummaryPush(result as RequestResult<DashboardSummary>);
      startedAtRef.current.delete(payload.requestId);
      if (started) addEntry('done', `Tổng quan cập nhật`, `SSE push · ${fmtElapsed(Date.now() - started)}`);
      markDoneIfComplete();
    }
    if (payload.requestId === regionsReqId) {
      setRegionsPush(result as RequestResult<RegionalPerformance>);
      startedAtRef.current.delete(payload.requestId);
      markDoneIfComplete();
    }
  }, true);

  useSignalREvent('RequestFailed', (payload: RequestFailedEvent) => {
    if (payload.requestId === summaryReqId || payload.requestId === regionsReqId) {
      startedAtRef.current.delete(payload.requestId);
      addEntry('error', 'Báo cáo thất bại', payload.error?.message ?? payload.status);
      markDoneIfComplete();
    }
  }, true);

  // ── Detect when polling resolves (fallback path) ──────────────────────────────
  // When summaryPolled arrives and we're still refreshing, mark done.
  const prevSummaryPolledRef = useRef<typeof summaryPolled>(undefined);
  useEffect(() => {
    if (
      summaryPolled &&
      summaryPolled !== prevSummaryPolledRef.current &&
      summaryPolled.status !== 'Processing' &&
      !summaryPush
    ) {
      prevSummaryPolledRef.current = summaryPolled;
      const started = summaryReqId ? startedAtRef.current.get(summaryReqId) : undefined;
      addEntry('done', `Tổng quan cập nhật`, `HTTP poll${started ? ` · ${fmtElapsed(Date.now() - started)}` : ''}`);
      markDoneIfComplete();
    }
  }, [summaryPolled, summaryPush, summaryReqId, addEntry, markDoneIfComplete]);

  const prevRegionsPolledRef = useRef<typeof regionsPolled>(undefined);
  useEffect(() => {
    if (
      regionsPolled &&
      regionsPolled !== prevRegionsPolledRef.current &&
      regionsPolled.status !== 'Processing' &&
      !regionsPush
    ) {
      prevRegionsPolledRef.current = regionsPolled;
      markDoneIfComplete();
    }
  }, [regionsPolled, regionsPush, markDoneIfComplete]);

  // ── Update "previous" snapshots when data changes ─────────────────────────────
  useEffect(() => {
    if (summary) {
      setPrevSummary(s => {
        if (!s) return null;           // don't set prev on first load
        return s;
      });
    }
  }, [summary]);

  const prevSummaryRef2 = useRef<DashboardSummary | null>(null);
  useEffect(() => {
    if (summary && prevSummaryRef2.current) setPrevSummary(prevSummaryRef2.current);
    if (summary) prevSummaryRef2.current = summary;
  }, [summary]);

  const prevRegionsMapRef = useRef<Record<string, RegionPerformanceRow>>({});
  useEffect(() => {
    if (regions.length > 0) {
      setPrevRegions(Object.values(prevRegionsMapRef.current));
      const next: Record<string, RegionPerformanceRow> = {};
      regions.forEach(r => { next[r.name] = r; });
      prevRegionsMapRef.current = next;
    }
  }, [regions]);

  // ── Widget stale handler ───────────────────────────────────────────────────────

  const handleWidgetStale = useCallback((payload: WidgetStaleEvent) => {
    setPushCount(c => c + 1);
    addEntry('push', 'Excel-provider đẩy dữ liệu mới',
      [payload.reason, payload.updatedAt].filter(Boolean).join(' · ') || 'datasource.updated');
    void refresh();
  }, [addEntry, refresh]);

  useWidgetSubscription(WIDGET_CHANNEL, handleWidgetStale, true);

  // ── Initial load on mount ─────────────────────────────────────────────────────

  useEffect(() => {
    addEntry('startup', 'Trang khởi động — chờ SSE và dữ liệu ban đầu');
    void refresh();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Render ────────────────────────────────────────────────────────────────────

  const hasData = !!summary;

  return (
    <div className="flex flex-col gap-5 min-h-0">

      {/* ── Header ── */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Theo dõi đồng bộ dữ liệu</h1>
          <p className="mt-0.5 text-sm text-gray-500">
            Dữ liệu từ excel-provider tự động cập nhật qua SSE khi có thay đổi
          </p>
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          {/* SSE state badge */}
          <div className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-medium border ${
            sseOpen
              ? 'border-green-200 bg-green-50 text-green-700'
              : 'border-amber-200 bg-amber-50 text-amber-700'
          }`}>
            <SignalIcon open={sseOpen} />
            <span className={sseColor}>{sseLabel}</span>
          </div>

          {/* Push counter */}
          {pushCount > 0 && (
            <div className="flex items-center gap-1.5 rounded-full border border-indigo-200 bg-indigo-50 px-3 py-1.5 text-xs font-medium text-indigo-700">
              <span className="h-2 w-2 rounded-full bg-indigo-500 animate-pulse" />
              {pushCount} push nhận được
            </div>
          )}

          <button
            onClick={() => { void refresh(); }}
            disabled={isRefreshing}
            className="flex items-center gap-2 rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-sm font-medium text-gray-600 shadow-sm hover:bg-gray-50 disabled:opacity-50 transition-colors"
          >
            <RefreshIcon spin={isRefreshing} />
            Làm mới
          </button>
        </div>
      </div>

      {/* SSE diagnostic bar (show when not open) */}
      {!sseOpen && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 flex items-start gap-3">
          <span className="text-lg shrink-0">⚠</span>
          <div>
            <p className="font-medium">SSE chưa kết nối — dữ liệu dùng HTTP polling (3 s)</p>
            <p className="mt-1 text-xs text-amber-700">
              Trạng thái: <strong>{sseLabel}</strong>.
              {esState === 0 && ' EventSource đang thử kết nối tới /sse/events — kiểm tra gateway và request-api đã deploy chưa.'}
              {esState === 2 && ' EventSource bị đóng — có thể lỗi 401/404/502. Mở DevTools → Network → filter "eventsource" để xem chi tiết.'}
              {esState === null && ' sseClient chưa được khởi tạo.'}
            </p>
            <p className="mt-1 text-xs text-amber-600 font-mono">
              Lệnh deploy: docker compose build request-api gateway &amp;&amp; docker compose up -d request-api gateway
            </p>
          </div>
        </div>
      )}

      {/* ── Two-column layout ── */}
      <div className="grid grid-cols-[290px_1fr] gap-5 flex-1 min-h-0">

        {/* Left: timeline */}
        <div className="flex flex-col rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
          <div className="flex items-center justify-between border-b border-gray-100 px-4 py-3">
            <span className="text-sm font-semibold text-gray-700">Lịch sử sự kiện</span>
            {timeline.length > 0 && (
              <button onClick={() => setTimeline([])} className="text-xs text-gray-400 hover:text-gray-600">
                Xóa
              </button>
            )}
          </div>

          <div className="flex-1 overflow-y-auto divide-y divide-gray-50">
            {timeline.length === 0 ? (
              <p className="p-6 text-center text-sm text-gray-400">Chưa có sự kiện nào</p>
            ) : (
              timeline.map(entry => (
                <div key={entry.id} className="px-4 py-3 hover:bg-gray-50 transition-colors">
                  <div className="flex items-center justify-between">
                    <KindBadge kind={entry.kind} />
                    <span className="text-xs text-gray-400 tabular-nums">{fmtTime(entry.ts)}</span>
                  </div>
                  <p className="mt-1 text-xs text-gray-700">{entry.headline}</p>
                  {entry.detail && (
                    <p className="mt-0.5 text-[11px] text-gray-400 font-mono leading-snug">{entry.detail}</p>
                  )}
                </div>
              ))
            )}
          </div>

          <div className="border-t border-gray-100 px-4 py-2.5 space-y-1">
            <p className="text-[11px] text-gray-400 font-mono truncate" title={WIDGET_CHANNEL}>
              📡 {WIDGET_CHANNEL}
            </p>
            <p className="text-[11px] text-gray-400">
              Push: <span className="font-medium text-indigo-600">{pushCount}</span> ·
              Polling: <span className="font-medium">{sseOpen ? 'tắt' : 'bật (3 s)'}</span>
            </p>
          </div>
        </div>

        {/* Right: data */}
        <div className="flex flex-col gap-5 min-w-0 overflow-y-auto">

          {/* Refresh status */}
          <div className="flex items-center justify-between min-h-[20px]">
            <p className="text-xs text-gray-400">
              {lastRefreshAt
                ? `Cập nhật lúc: ${fmtTime(lastRefreshAt)}`
                : (isRefreshing ? 'Đang tải dữ liệu lần đầu…' : '')}
            </p>
            {isRefreshing && (
              <span className="flex items-center gap-1.5 text-xs text-amber-600 font-medium animate-pulse">
                <RefreshIcon spin /> Đang làm mới…
              </span>
            )}
          </div>

          {/* KPI cards */}
          <section>
            <h2 className="mb-3 text-xs font-semibold text-gray-500 uppercase tracking-widest">
              Tổng quan Dashboard
            </h2>
            {hasData ? (
              <div className="grid grid-cols-2 xl:grid-cols-4 gap-4">
                <KpiCard label="Doanh thu" value={summary!.totalRevenue}
                  prev={prevSummary?.totalRevenue} unit="VNĐ" />
                <KpiCard label="Số lượng" value={summary!.totalUnits}
                  prev={prevSummary?.totalUnits} />
                <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
                  <p className="text-xs font-medium uppercase tracking-wider text-gray-400">Top khu vực</p>
                  <p className="mt-1.5 text-2xl font-bold text-gray-800">{summary!.topRegion}</p>
                  {prevSummary && prevSummary.topRegion !== summary!.topRegion && (
                    <p className="mt-0.5 text-xs text-indigo-500 font-mono">← {prevSummary.topRegion}</p>
                  )}
                </div>
                <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
                  <p className="text-xs font-medium uppercase tracking-wider text-gray-400">Top sản phẩm</p>
                  <p className="mt-1.5 text-lg font-bold text-gray-800 truncate">{summary!.topProduct}</p>
                  {(summary!.alerts?.length ?? 0) > 0 && (
                    <p className="mt-1 text-xs text-amber-600">⚠ {summary!.alerts.length} cảnh báo</p>
                  )}
                </div>
              </div>
            ) : (
              <div className="grid grid-cols-2 xl:grid-cols-4 gap-4">
                {[...Array(4)].map((_, i) => <SkeletonCard key={i} />)}
              </div>
            )}
          </section>

          {/* Alerts */}
          {(summary?.alerts?.length ?? 0) > 0 && (
            <section>
              <h2 className="mb-2 text-xs font-semibold text-gray-500 uppercase tracking-widest">
                Cảnh báo ({summary!.alerts.length})
              </h2>
              <div className="space-y-1.5">
                {summary!.alerts.map((a, i) => (
                  <div key={i} className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                    <span className="mt-0.5 shrink-0">⚠</span>
                    <span>{a}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Regional performance */}
          <section>
            <h2 className="mb-3 text-xs font-semibold text-gray-500 uppercase tracking-widest">
              Hiệu suất theo khu vực (hôm nay)
            </h2>
            <div className="rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
              {regions.length > 0 ? (
                <table className="w-full">
                  <thead>
                    <tr className="border-b border-gray-100 bg-gray-50">
                      <th className="py-2.5 px-4 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">Khu vực</th>
                      <th className="py-2.5 px-4 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">Doanh thu</th>
                      <th className="py-2.5 px-4 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">SL</th>
                      <th className="py-2.5 px-4 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">Đạt mục tiêu</th>
                    </tr>
                  </thead>
                  <tbody>
                    {regions.map(row => (
                      <RegionRow key={row.name} row={row}
                        prev={prevRegions.find(r => r.name === row.name)} />
                    ))}
                  </tbody>
                </table>
              ) : (
                <div className="p-6 animate-pulse space-y-3">
                  {[...Array(4)].map((_, i) => (
                    <div key={i} className="flex gap-4">
                      <div className="h-3 flex-1 rounded bg-gray-200" />
                      <div className="h-3 w-20 rounded bg-gray-200" />
                      <div className="h-3 w-16 rounded bg-gray-200" />
                      <div className="h-3 w-24 rounded bg-gray-200" />
                    </div>
                  ))}
                </div>
              )}
            </div>
          </section>

          {/* Change legend */}
          {(prevSummary || prevRegions.length > 0) && (
            <p className="text-xs text-gray-400">
              <ArrowUpIcon /><ArrowDownIcon /> Thay đổi so với lần cập nhật trước
            </p>
          )}

        </div>
      </div>
    </div>
  );
}
