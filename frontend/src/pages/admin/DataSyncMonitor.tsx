import { useState, useCallback, useRef, useEffect } from 'react';
import { useAuth } from 'react-oidc-context';
import { useSignalREvent, useWidgetSubscription } from '../../hooks/useSignalR';
import { submitRequest, mapPushToResult } from '../../api/requests';
import { hasRealmRole } from '../../api/client';
// hasRealmRole reads token from sessionStorage directly (no user arg)
import { sseClient } from '../../api/sse';
import type {
  RequestCompletedEvent,
  RequestFailedEvent,
  WidgetStaleEvent,
  DashboardSummary,
  RegionalPerformance,
  RegionPerformanceRow,
} from '../../types/contracts';

const WIDGET_CHANNEL = 'widget:main-dashboard:main-dashboard';

// ── Types ─────────────────────────────────────────────────────────────────────

type EventKind = 'connected' | 'push' | 'refreshing' | 'done' | 'error';

interface TimelineEntry {
  id: string;
  ts: Date;
  kind: EventKind;
  headline: string;
  detail?: string;
}

interface KpiSnapshot {
  totalRevenue: number;
  totalUnits: number;
  topRegion: string;
  topProduct: string;
  alerts: string[];
}

interface PendingRequest {
  operation: 'summary' | 'regions';
  startedAt: number; // Date.now()
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(n: number) {
  return new Intl.NumberFormat('vi-VN').format(n);
}

function fmtTime(d: Date) {
  return d.toLocaleTimeString('vi-VN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function fmtElapsed(ms: number) {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`;
}

function delta(next: number, prev: number | undefined) {
  if (prev === undefined || next === prev) return null;
  return next > prev ? 'up' : 'down';
}

// ── Icon components ───────────────────────────────────────────────────────────

function SignalIcon({ connected }: { connected: boolean }) {
  return (
    <svg className={`h-4 w-4 ${connected ? 'text-green-400' : 'text-gray-400'}`}
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
  return (
    <svg className="inline h-3.5 w-3.5 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 15l7-7 7 7" />
    </svg>
  );
}

function ArrowDownIcon() {
  return (
    <svg className="inline h-3.5 w-3.5 text-red-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M19 9l-7 7-7-7" />
    </svg>
  );
}

// ── Timeline dot / badge ──────────────────────────────────────────────────────

function KindBadge({ kind }: { kind: EventKind }) {
  const cfg: Record<EventKind, { dot: string; label: string }> = {
    connected:  { dot: 'bg-gray-400',   label: 'Kết nối'    },
    push:       { dot: 'bg-indigo-500', label: 'Excel push'  },
    refreshing: { dot: 'bg-amber-400 animate-pulse', label: 'Làm mới…' },
    done:       { dot: 'bg-green-500',  label: 'Đã cập nhật' },
    error:      { dot: 'bg-red-500',    label: 'Lỗi'         },
  };
  const { dot, label } = cfg[kind];
  return (
    <span className="flex items-center gap-1.5">
      <span className={`inline-block h-2.5 w-2.5 rounded-full shrink-0 ${dot}`} />
      <span className="text-xs font-medium">{label}</span>
    </span>
  );
}

// ── KPI card ──────────────────────────────────────────────────────────────────

function KpiCard({
  label, value, prev, unit = '', format = fmt,
}: {
  label: string;
  value: number | string;
  prev?: number;
  unit?: string;
  format?: (n: number) => string;
}) {
  const num = typeof value === 'number' ? value : undefined;
  const dir = num !== undefined ? delta(num, prev) : null;

  return (
    <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wider text-gray-400">{label}</p>
      <p className="mt-1 text-2xl font-bold text-gray-800">
        {num !== undefined ? format(num) : value}
        {unit && <span className="ml-1 text-sm font-normal text-gray-400">{unit}</span>}
        {dir === 'up'   && <span className="ml-2"><ArrowUpIcon /></span>}
        {dir === 'down' && <span className="ml-2"><ArrowDownIcon /></span>}
      </p>
    </div>
  );
}

// ── Regional row ──────────────────────────────────────────────────────────────

function RegionRow({
  row, prev,
}: {
  row: RegionPerformanceRow;
  prev?: RegionPerformanceRow;
}) {
  const dir = prev ? delta(row.achievementPct, prev.achievementPct) : null;
  const pct = Math.min(100, Math.max(0, row.achievementPct));

  return (
    <tr className="border-b border-gray-100 last:border-0 hover:bg-gray-50 transition-colors">
      <td className="py-2 pr-3 text-sm font-medium text-gray-800">{row.name}</td>
      <td className="py-2 pr-3 text-sm text-gray-600 text-right tabular-nums">
        {fmt(row.revenue)}
      </td>
      <td className="py-2 pr-3 text-sm text-gray-500 text-right tabular-nums">
        {fmt(row.units)}
      </td>
      <td className="py-2 text-right">
        <div className="flex items-center justify-end gap-2">
          <div className="h-1.5 w-20 rounded-full bg-gray-200">
            <div
              className={`h-1.5 rounded-full transition-all duration-500 ${
                pct >= 80 ? 'bg-green-500' : pct >= 60 ? 'bg-amber-400' : 'bg-red-400'
              }`}
              style={{ width: `${pct}%` }}
            />
          </div>
          <span className={`text-xs font-semibold w-10 text-right ${
            pct >= 80 ? 'text-green-600' : pct >= 60 ? 'text-amber-600' : 'text-red-500'
          }`}>
            {pct.toFixed(1)}%
          </span>
          {dir === 'up'   && <ArrowUpIcon />}
          {dir === 'down' && <ArrowDownIcon />}
        </div>
      </td>
    </tr>
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

  // ── State ────────────────────────────────────────────────────────────────────

  const [timeline,    setTimeline]    = useState<TimelineEntry[]>([]);
  const [sseOk,       setSseOk]       = useState(false);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastRefreshAt, setLastRefreshAt] = useState<Date | null>(null);
  const [pushCount,   setPushCount]   = useState(0);

  const [summary,     setSummary]     = useState<KpiSnapshot | null>(null);
  const [prevSummary, setPrevSummary] = useState<KpiSnapshot | null>(null);

  const [regions,     setRegions]     = useState<RegionPerformanceRow[]>([]);
  const [prevRegions, setPrevRegions] = useState<RegionPerformanceRow[]>([]);

  // ── Refs (avoid stale closures in always-enabled SSE handlers) ────────────────

  const isRefreshingRef = useRef(false);
  const pendingRef      = useRef(new Map<string, PendingRequest>());
  const addEntry = useCallback((kind: EventKind, headline: string, detail?: string) => {
    setTimeline(prev => [
      { id: crypto.randomUUID(), ts: new Date(), kind, headline, detail },
      ...prev.slice(0, 99),
    ]);
  }, []);

  // ── Refresh logic ─────────────────────────────────────────────────────────────

  const refresh = useCallback(async () => {
    if (isRefreshingRef.current) return;
    isRefreshingRef.current = true;
    setIsRefreshing(true);

    try {
      const [ackS, ackR] = await Promise.all([
        submitRequest({
          operation: 'report.dashboard.summary',
          params: {},
          tenantId,
          userId,
          priority: 'High',
        }),
        submitRequest({
          operation: 'report.regional.performance',
          params: { period: 'today' },
          tenantId,
          userId,
          priority: 'High',
        }),
      ]);

      const now = Date.now();
      pendingRef.current.set(ackS.requestId, { operation: 'summary', startedAt: now });
      pendingRef.current.set(ackR.requestId, { operation: 'regions', startedAt: now });
      addEntry('refreshing', 'Đang lấy dữ liệu mới…');
    } catch (err) {
      isRefreshingRef.current = false;
      setIsRefreshing(false);
      addEntry('error', 'Gửi yêu cầu thất bại', err instanceof Error ? err.message : String(err));
    }
  }, [tenantId, userId, addEntry]);

  // ── SignalR / SSE event handlers ──────────────────────────────────────────────

  useSignalREvent('RequestCompleted', (payload: RequestCompletedEvent) => {
    const pending = pendingRef.current.get(payload.requestId);
    if (!pending) return;

    pendingRef.current.delete(payload.requestId);
    const elapsed = Date.now() - pending.startedAt;
    const result  = mapPushToResult(payload);

    if (result.status === 'Completed') {
      if (pending.operation === 'summary' && result.data) {
        const d = result.data as DashboardSummary;
        setSummary(s => { setPrevSummary(s); return d; });
      }
      if (pending.operation === 'regions' && result.data) {
        const d = result.data as RegionalPerformance;
        setRegions(r => { setPrevRegions(r); return d.regions ?? []; });
      }
    }

    if (pendingRef.current.size === 0) {
      isRefreshingRef.current = false;
      setIsRefreshing(false);
      setLastRefreshAt(new Date());
      addEntry('done', `Dữ liệu cập nhật thành công`, `Thời gian: ${fmtElapsed(elapsed)}`);
    }
  }, true);

  useSignalREvent('RequestFailed', (payload: RequestFailedEvent) => {
    const pending = pendingRef.current.get(payload.requestId);
    if (!pending) return;
    pendingRef.current.delete(payload.requestId);
    if (pendingRef.current.size === 0) {
      isRefreshingRef.current = false;
      setIsRefreshing(false);
    }
    addEntry('error', 'Báo cáo thất bại', payload.error?.message ?? payload.status);
  }, true);

  // ── Widget stale — excel-provider đẩy dữ liệu mới ───────────────────────────

  const handleWidgetStale = useCallback((payload: WidgetStaleEvent) => {
    setPushCount(c => c + 1);
    addEntry('push', 'Excel-provider đẩy dữ liệu mới', payload.reason ?? 'datasource.updated');
    void refresh();
  }, [addEntry, refresh]);

  useWidgetSubscription(WIDGET_CHANNEL, handleWidgetStale, true);

  // ── SSE connection polling ────────────────────────────────────────────────────

  useEffect(() => {
    const id = setInterval(() => setSseOk(sseClient.isConnected), 2000);
    setSseOk(sseClient.isConnected);
    return () => clearInterval(id);
  }, []);

  // ── Initial load ──────────────────────────────────────────────────────────────

  useEffect(() => {
    addEntry('connected', 'SSE kết nối — đang theo dõi dữ liệu');
    void refresh();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6 min-h-0">

      {/* ── Header bar ── */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">
            Theo dõi đồng bộ dữ liệu
          </h1>
          <p className="mt-0.5 text-sm text-gray-500">
            Dữ liệu từ excel-provider tự động cập nhật khi có thay đổi
          </p>
        </div>

        {/* Status + manual refresh */}
        <div className="flex items-center gap-3">
          <div className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-medium border ${
            sseOk
              ? 'border-green-200 bg-green-50 text-green-700'
              : 'border-gray-200 bg-gray-50 text-gray-500'
          }`}>
            <SignalIcon connected={sseOk} />
            {sseOk ? 'SSE connected' : 'SSE connecting…'}
          </div>

          {pushCount > 0 && (
            <div className="flex items-center gap-1.5 rounded-full border border-indigo-200 bg-indigo-50 px-3 py-1.5 text-xs font-medium text-indigo-700">
              <span className="h-2 w-2 rounded-full bg-indigo-500 animate-pulse" />
              {pushCount} lần nhận dữ liệu
            </div>
          )}

          <button
            onClick={() => { addEntry('refreshing', 'Làm mới thủ công'); void refresh(); }}
            disabled={isRefreshing}
            className="flex items-center gap-2 rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-sm font-medium text-gray-600 shadow-sm hover:bg-gray-50 disabled:opacity-50 transition-colors"
          >
            <RefreshIcon spin={isRefreshing} />
            Làm mới
          </button>
        </div>
      </div>

      {/* ── Two-column layout ── */}
      <div className="grid grid-cols-[300px_1fr] gap-6 min-h-0">

        {/* Left: timeline */}
        <div className="flex flex-col rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
          <div className="flex items-center justify-between border-b border-gray-100 px-4 py-3">
            <span className="text-sm font-semibold text-gray-700">Lịch sử sự kiện</span>
            {timeline.length > 0 && (
              <button
                onClick={() => setTimeline([])}
                className="text-xs text-gray-400 hover:text-gray-600"
              >
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
                    <p className="mt-0.5 text-xs text-gray-400 font-mono">{entry.detail}</p>
                  )}
                </div>
              ))
            )}
          </div>

          {/* Channel badge */}
          <div className="border-t border-gray-100 px-4 py-2">
            <p className="text-xs text-gray-400 font-mono truncate" title={WIDGET_CHANNEL}>
              📡 {WIDGET_CHANNEL}
            </p>
          </div>
        </div>

        {/* Right: data panels */}
        <div className="flex flex-col gap-5 min-w-0">

          {/* Last refresh timestamp */}
          <div className="flex items-center justify-between">
            <p className="text-xs text-gray-400">
              {lastRefreshAt
                ? `Cập nhật lần cuối: ${fmtTime(lastRefreshAt)}`
                : 'Đang tải dữ liệu…'}
            </p>
            {isRefreshing && (
              <span className="flex items-center gap-1.5 text-xs text-amber-600 font-medium animate-pulse">
                <RefreshIcon spin />
                Đang làm mới…
              </span>
            )}
          </div>

          {/* KPI cards */}
          <section>
            <h2 className="mb-3 text-sm font-semibold text-gray-600 uppercase tracking-wide">
              Tổng quan Dashboard
            </h2>
            {summary ? (
              <div className="grid grid-cols-2 xl:grid-cols-4 gap-4">
                <KpiCard
                  label="Doanh thu"
                  value={summary.totalRevenue}
                  prev={prevSummary?.totalRevenue}
                  unit="VNĐ"
                />
                <KpiCard
                  label="Số lượng"
                  value={summary.totalUnits}
                  prev={prevSummary?.totalUnits}
                />
                <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
                  <p className="text-xs font-medium uppercase tracking-wider text-gray-400">Top khu vực</p>
                  <p className="mt-1 text-2xl font-bold text-gray-800">{summary.topRegion}</p>
                  {prevSummary && prevSummary.topRegion !== summary.topRegion && (
                    <p className="mt-0.5 text-xs text-indigo-500">
                      ← {prevSummary.topRegion}
                    </p>
                  )}
                </div>
                <div className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
                  <p className="text-xs font-medium uppercase tracking-wider text-gray-400">Top sản phẩm</p>
                  <p className="mt-1 text-lg font-bold text-gray-800 truncate">{summary.topProduct}</p>
                  {summary.alerts.length > 0 && (
                    <p className="mt-1 text-xs text-amber-600">
                      ⚠ {summary.alerts.length} cảnh báo
                    </p>
                  )}
                </div>
              </div>
            ) : (
              <div className="grid grid-cols-2 xl:grid-cols-4 gap-4">
                {[...Array(4)].map((_, i) => (
                  <div key={i} className="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm animate-pulse">
                    <div className="h-3 w-20 rounded bg-gray-200 mb-3" />
                    <div className="h-7 w-28 rounded bg-gray-200" />
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* Alerts */}
          {(summary?.alerts?.length ?? 0) > 0 && (
            <section>
              <h2 className="mb-2 text-sm font-semibold text-gray-600 uppercase tracking-wide">
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
          <section className="flex-1">
            <h2 className="mb-3 text-sm font-semibold text-gray-600 uppercase tracking-wide">
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
                  <tbody className="px-4">
                    {regions.map(row => (
                      <RegionRow
                        key={row.name}
                        row={row}
                        prev={prevRegions.find(r => r.name === row.name)}
                      />
                    ))}
                  </tbody>
                </table>
              ) : (
                <div className="p-8 text-center">
                  <div className="animate-pulse space-y-3">
                    {[...Array(4)].map((_, i) => (
                      <div key={i} className="flex gap-4">
                        <div className="h-3 flex-1 rounded bg-gray-200" />
                        <div className="h-3 w-20 rounded bg-gray-200" />
                        <div className="h-3 w-16 rounded bg-gray-200" />
                        <div className="h-3 w-24 rounded bg-gray-200" />
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </section>

          {/* Change diff note */}
          {prevSummary && (
            <p className="text-xs text-gray-400 flex items-center gap-1.5">
              <ArrowUpIcon />/<ArrowDownIcon />
              &nbsp;Chỉ thị thay đổi so với lần cập nhật trước
            </p>
          )}

        </div>
      </div>
    </div>
  );
}
