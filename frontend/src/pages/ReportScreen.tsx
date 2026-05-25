import { useEffect, useState, useRef } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { apiGet, apiPost, getUserClaims } from '../api/client';
import { submitRequest, pollResult } from '../api/requests';
import { sseClient } from '../api/sse';
import type { MenuDetail, ScreenDetail, WidgetDef, WidgetConfig } from '../types/menuTypes';

// ---------------------------------------------------------------------------
// Data format conventions (quy tắc chung cho từng loại chart):
//
//  kpi   → { value: number|string, label?: string, trend?: number|string, unit?: string }
//  line  → { labels: string[], series: { name: string, data: number[] }[] }
//  bar   → { labels: string[], series: { name: string, data: number[] }[] }
//         OR array of rows: uses config.xField + config.yField for mapping
//  pie   → { labels: string[], values: number[] }
//         OR array of rows: uses config.catField + config.valField
//  table → { columns?: { key: string, label: string }[], rows: Record<string,unknown>[] }
//         OR plain Record<string,unknown>[] (columns auto-detected from first row)
//  text  → { text: string } | string
//
// Nếu trả sai định dạng → widget hiển thị thông báo "Dữ liệu không đúng định dạng"
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// useWidgetData — submit operation, poll result
// ---------------------------------------------------------------------------
type DataStatus = 'idle' | 'loading' | 'done' | 'error';

function useWidgetData(widget: WidgetDef, refreshKey: number) {
  const [status, setStatus]   = useState<DataStatus>('idle');
  const [data,   setData]     = useState<unknown>(null);
  const [error,  setError]    = useState<string | null>(null);
  const abortRef = useRef(false);

  useEffect(() => {
    if (!widget.dataSource) { setStatus('idle'); return; }

    let cfg: WidgetConfig = {};
    try { cfg = JSON.parse(widget.config) as WidgetConfig; } catch { /* ignore */ }

    const { userId, tenantId } = getUserClaims();
    abortRef.current = false;
    setStatus('loading');
    setData(null);
    setError(null);

    void (async () => {
      try {
        const ack = await submitRequest({
          operation:    widget.dataSource!,
          params:       (cfg.params as Record<string, unknown>) ?? {},
          tenantId,
          userId,
          cacheSeconds: 60,
        });
        if (abortRef.current) return;

        const result = await pollResult(ack.requestId, 1500, 40);
        if (abortRef.current) return;

        if (result.status === 'Completed') {
          setData(result.data ?? null);
          setStatus('done');
        } else {
          setError(result.error ?? 'Thực thi thất bại');
          setStatus('error');
        }
      } catch (e) {
        if (!abortRef.current) {
          setError(e instanceof Error ? e.message : 'Lỗi không xác định');
          setStatus('error');
        }
      }
    })();

    return () => { abortRef.current = true; };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [widget.id, widget.dataSource, widget.config, refreshKey]);

  return { status, data, error };
}

// ---------------------------------------------------------------------------
// Chart renderers
// ---------------------------------------------------------------------------
const CHART_COLORS = ['#4f46e5','#0ea5e9','#10b981','#f59e0b','#ef4444','#8b5cf6'];

interface LineSeries { name: string; data: number[] }
interface LineData   { labels: string[]; series: LineSeries[] }

function isLineData(d: unknown): d is LineData {
  if (!d || typeof d !== 'object') return false;
  const o = d as Record<string, unknown>;
  return Array.isArray(o.labels) && Array.isArray(o.series) && o.series.length > 0;
}

function fmtAxisVal(v: number): string {
  return v.toLocaleString('vi-VN');
}

function renderLine(data: unknown, color: string, cfg: WidgetConfig): React.ReactNode {
  // Normalise: standard {labels, series[]} OR row-array with xField/yField
  let lineData: LineData | null = null;

  if (isLineData(data)) {
    lineData = data;
  } else if (Array.isArray(data) && cfg.xField && cfg.yField) {
    const rows = data as Record<string, unknown>[];
    lineData = {
      labels: rows.map(r => String(r[cfg.xField!] ?? '')),
      series: [{ name: cfg.yField!, data: rows.map(r => Number(r[cfg.yField!] ?? 0)) }],
    };
  }

  if (!lineData) return <FormatError />;

  const { labels, series } = lineData;
  const H = 80, W = 300, pad = 5;
  const toX = (i: number) => pad + (i / Math.max(labels.length - 1, 1)) * (W - pad * 2);

  // Mỗi series dùng Y-scale riêng → tránh series nhỏ bị flatten khi cùng trục với series lớn
  const seriesScaled = series.map((s, si) => {
    const mn = Math.min(...s.data, 0);
    const mx = Math.max(...s.data, 1);
    const toY = (v: number) => H - pad - ((v - mn) / (mx - mn || 1)) * (H - pad * 2);
    const pts = s.data.map((v, i) => `${toX(i)},${toY(v)}`).join(' ');
    const fill = `${pts} ${toX(s.data.length - 1)},${H} ${toX(0)},${H}`;
    const c = CHART_COLORS[si % CHART_COLORS.length] ?? color;
    const maxV = Math.max(...s.data);
    return { s, si, pts, fill, c, maxV };
  });

  // Thin out labels — show ~6 evenly spaced (tránh chồng chéo với 30 điểm)
  const step = Math.ceil(labels.length / 6);
  const shownLabels = labels
    .map((l, i) => ({ l, i }))
    .filter(({ i }) => i === 0 || i === labels.length - 1 || i % step === 0);

  return (
    <div>
      <svg viewBox={`0 0 ${W} ${H}`} className="w-full" style={{ height: H }} preserveAspectRatio="none">
        <defs>
          {seriesScaled.map(({ si, c }) => (
            <linearGradient key={si} id={`lg-${si}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={c} stopOpacity=".2"/>
              <stop offset="100%" stopColor={c} stopOpacity="0"/>
            </linearGradient>
          ))}
        </defs>
        {seriesScaled.map(({ si, pts, fill, c }) => (
          <g key={si}>
            <polygon points={fill} fill={`url(#lg-${si})`} />
            <polyline points={pts} fill="none" stroke={c} strokeWidth="1.8" />
          </g>
        ))}
      </svg>

      {/* X-axis labels — evenly thinned */}
      {labels.length >= 2 && (
        <div className="relative mt-0.5 h-4">
          {shownLabels.map(({ l, i }) => (
            <span
              key={i}
              className="absolute text-[9px] text-gray-400 -translate-x-1/2"
              style={{ left: `${(i / (labels.length - 1)) * 100}%` }}
            >
              {l}
            </span>
          ))}
        </div>
      )}

      {/* Legend — chỉ hiện khi nhiều hơn 1 series */}
      {series.length > 1 && (
        <div className="mt-2 flex flex-wrap gap-3">
          {seriesScaled.map(({ s, si, c, maxV }) => (
            <div key={si} className="flex items-center gap-1.5 text-[10px]">
              <span className="inline-block w-3 h-0.5 rounded" style={{ background: c }} />
              <span className="text-gray-500">{s.name}</span>
              <span className="text-gray-400 tabular-nums">max {fmtAxisVal(maxV)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function renderBar(data: unknown, color: string, cfg: WidgetConfig): React.ReactNode {
  let rows: [string, number][] = [];

  if (isLineData(data)) {
    rows = data.labels.map((l, i) => [l, data.series[0]?.data[i] ?? 0]);
  } else if (Array.isArray(data) && cfg.xField && cfg.yField) {
    rows = (data as Record<string, unknown>[]).map(r => [
      String(r[cfg.xField!] ?? ''), Number(r[cfg.yField!] ?? 0),
    ]);
  } else {
    return <FormatError />;
  }

  const mx = Math.max(...rows.map(r => r[1]), 1);
  return (
    <div className="flex flex-col gap-1.5 mt-1">
      {rows.slice(0, 8).map(([l, v], i) => (
        <div key={i} className="flex items-center gap-2 text-[11px]">
          <span className="w-24 shrink-0 text-right text-gray-500 truncate">{l}</span>
          <div className="flex-1 bg-gray-100 rounded-full h-2.5 overflow-hidden">
            <div className="h-full rounded-full transition-all" style={{ width: `${(v/mx)*100}%`, background: color }} />
          </div>
          <span className="w-16 text-right text-gray-600 font-medium tabular-nums">
            {typeof v === 'number' ? v.toLocaleString('vi-VN') : String(v)}
          </span>
        </div>
      ))}
    </div>
  );
}

function renderPie(data: unknown, cfg: WidgetConfig): React.ReactNode {
  let labels: string[] = [];
  let values: number[] = [];

  if (data && typeof data === 'object' && !Array.isArray(data)) {
    const o = data as Record<string, unknown>;
    if (Array.isArray(o.labels) && Array.isArray(o.values)) {
      labels = o.labels as string[];
      values = o.values as number[];
    }
  } else if (Array.isArray(data) && cfg.catField && cfg.valField) {
    const rows = data as Record<string, unknown>[];
    labels = rows.map(r => String(r[cfg.catField!] ?? ''));
    values = rows.map(r => Number(r[cfg.valField!] ?? 0));
  }

  if (!labels.length || !values.length) return <FormatError />;

  const total = values.reduce((s, v) => s + v, 0) || 1;
  const R = 45, CX = 60, CY = 60, circ = 2 * Math.PI * R;
  let offset = 0;

  return (
    <div className="flex items-center gap-4">
      <svg viewBox="0 0 120 120" className="shrink-0" style={{ width: 90, height: 90 }}>
        {values.map((v, i) => {
          const dash = (v / total) * circ;
          const seg = (
            <circle key={i} cx={CX} cy={CY} r={R} fill="none"
              stroke={CHART_COLORS[i % CHART_COLORS.length]}
              strokeWidth="22"
              strokeDasharray={`${dash} ${circ - dash}`}
              strokeDashoffset={-offset + circ * 0.25}
              transform={`rotate(-90 ${CX} ${CY})`}
            />
          );
          offset += dash;
          return seg;
        })}
        <circle cx={CX} cy={CY} r={R - 11} fill="white" />
        <text x={CX} y={CY + 4} textAnchor="middle" fontSize="9" fill="#374151" fontWeight="700">
          {labels[0]}
        </text>
      </svg>
      <div className="flex flex-col gap-1 min-w-0">
        {labels.slice(0, 5).map((l, i) => (
          <div key={i} className="flex items-center gap-1.5 text-[10px]">
            <span className="w-2 h-2 rounded-full shrink-0" style={{ background: CHART_COLORS[i % CHART_COLORS.length] }} />
            <span className="text-gray-600 truncate">{l}</span>
            <span className="ml-auto text-gray-400 tabular-nums shrink-0">{((values[i]!/total)*100).toFixed(0)}%</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function renderTable(data: unknown, cfg: WidgetConfig): React.ReactNode {
  type Row = Record<string, unknown>;
  let rows: Row[] = [];
  let cols: { key: string; label: string }[] = [];

  if (data && typeof data === 'object' && !Array.isArray(data)) {
    const o = data as Record<string, unknown>;
    if (Array.isArray(o.rows)) {
      // { columns?, rows[] } format
      rows = o.rows as Row[];
      if (Array.isArray(o.columns)) {
        cols = o.columns as { key: string; label: string }[];
      }
    } else {
      // Tìm array đầu tiên trong object (vd: alerts[], products[], regions[])
      const entry = Object.entries(o).find(([, v]) => Array.isArray(v));
      if (entry) {
        const arr = entry[1] as unknown[];
        if (arr.length > 0 && typeof arr[0] === 'string') {
          // string[] (vd: alerts) → bảng 1 cột
          rows = arr.map(s => ({ [entry[0]]: s }));
        } else {
          rows = arr as Row[];
        }
      }
    }
  } else if (Array.isArray(data)) {
    if (data.length > 0 && typeof data[0] === 'string') {
      rows = (data as string[]).map((s, i) => ({ '#': i + 1, value: s }));
    } else {
      rows = data as Row[];
    }
  }

  if (!rows.length) return <FormatError />;

  // Determine columns
  if (!cols.length) {
    const wanted = cfg.cols?.length ? cfg.cols : Object.keys(rows[0] ?? {});
    cols = wanted.map(k => ({ key: k, label: k }));
  }

  return (
    <div className="overflow-auto max-h-60 rounded border border-gray-100">
      <table className="w-full text-xs">
        <thead className="sticky top-0 bg-gray-50">
          <tr>
            {cols.map(c => (
              <th key={c.key} className="px-3 py-1.5 text-left font-semibold text-gray-500 border-b border-gray-100 whitespace-nowrap">
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-50">
          {rows.map((row, i) => (
            <tr key={i} className="hover:bg-gray-50 transition-colors">
              {cols.map(c => (
                <td key={c.key} className="px-3 py-1.5 text-gray-700 whitespace-nowrap">
                  {String(row[c.key] ?? '—')}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function renderKpi(data: unknown, color: string, cfg: WidgetConfig): React.ReactNode {
  let value: string | number = '—';
  let label: string | undefined;
  let trend: string | number | undefined;
  let unit: string | undefined;

  if (data && typeof data === 'object' && !Array.isArray(data)) {
    const o = data as Record<string, unknown>;

    if (cfg.valField) {
      // Admin đã chỉ định field cụ thể
      value = o[cfg.valField] as string | number ?? '—';
    } else if (typeof o.value !== 'undefined') {
      // Standard format: { value, label, trend, unit }
      value = o.value as string | number;
    } else {
      // Auto-pick: lấy field số đầu tiên trong object (bỏ qua nested object/array)
      const firstNum = Object.entries(o).find(
        ([, v]) => typeof v === 'number' && isFinite(v as number)
      );
      if (firstNum) value = firstNum[1] as number;
    }

    label = (o.label ?? (cfg.valField ? undefined : o.topProduct ?? o.topRegion)) as string | undefined;
    trend = (cfg.trendField ? o[cfg.trendField] : o.trend) as string | number | undefined;
    unit  = o.unit as string | undefined;
  } else if (typeof data === 'number' || typeof data === 'string') {
    value = data;
  }

  const numVal = typeof value === 'number' ? value : null;
  const display = numVal !== null
    ? numVal.toLocaleString('vi-VN')
    : String(value);

  const trendNum  = typeof trend === 'number' ? trend : null;
  const trendStr  = trend != null ? String(trend) : null;
  const positive  = trendNum !== null ? trendNum >= 0 : trendStr ? trendStr.startsWith('+') : null;

  return (
    <div className="flex flex-col gap-1 py-2">
      <span className="text-3xl font-black leading-none" style={{ color }}>
        {display}{unit && <span className="text-base ml-1 font-normal text-gray-400">{unit}</span>}
      </span>
      {label && <span className="text-xs text-gray-500">{label}</span>}
      {trendStr && (
        <span className={`text-xs font-medium ${positive === true ? 'text-green-600' : positive === false ? 'text-red-500' : 'text-gray-400'}`}>
          {positive === true ? '▲' : positive === false ? '▼' : '●'} {trendStr}
        </span>
      )}
    </div>
  );
}

function renderText(data: unknown): React.ReactNode {
  const text = data && typeof data === 'object' && !Array.isArray(data)
    ? (data as Record<string, unknown>).text as string | undefined
    : typeof data === 'string' ? data : null;
  if (!text) return <FormatError />;
  return <p className="text-sm text-gray-700 whitespace-pre-wrap leading-relaxed">{text}</p>;
}

function FormatError() {
  return (
    <p className="text-xs text-amber-600 italic">
      ⚠ Dữ liệu không đúng định dạng — kiểm tra lại operation hoặc field mapping.
    </p>
  );
}

// ---------------------------------------------------------------------------
// Data-source badge
// ---------------------------------------------------------------------------
function DataSourceBadge({ source }: { source: string }) {
  const [provider] = source.split('.');
  const cls =
    provider === 'sql'   ? 'bg-blue-100 text-blue-700' :
    provider === 'excel' ? 'bg-green-100 text-green-700' :
    provider === 'ml'    ? 'bg-purple-100 text-purple-700' :
                           'bg-gray-100 text-gray-600';
  return (
    <span className={`rounded px-1.5 py-0.5 text-[0.72rem] font-mono font-medium ${cls}`}>
      {source}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Widget card
// ---------------------------------------------------------------------------
function WidgetCard({ widget, refreshKey }: { widget: WidgetDef; refreshKey: number }) {
  const { status, data, error } = useWidgetData(widget, refreshKey);

  let cfg: WidgetConfig = {};
  try { cfg = JSON.parse(widget.config) as WidgetConfig; } catch { /* ignore */ }

  function renderBody() {
    // Loading
    if (status === 'loading') {
      return (
        <div className="flex items-center gap-2 py-4 text-gray-400 text-xs">
          <div className="h-4 w-4 animate-spin rounded-full border-2 border-gray-300 border-t-brand-500" />
          Đang lấy dữ liệu…
        </div>
      );
    }

    // Error
    if (status === 'error') {
      return (
        <div className="rounded-lg bg-red-50 border border-red-100 px-3 py-2 text-xs text-red-600">
          {error ?? 'Lỗi không xác định'}
        </div>
      );
    }

    // No data source configured
    if (!widget.dataSource) {
      return (
        <div className="flex h-24 items-center justify-center text-gray-300 text-sm italic">
          Chưa cấu hình operation
        </div>
      );
    }

    // Idle (no data yet — should not normally happen since useEffect fires immediately)
    if (status === 'idle') {
      return <div className="h-24 rounded bg-gray-50 animate-pulse" />;
    }

    // Render by type
    switch (widget.widgetType) {
      case 'kpi':   return renderKpi(data, widget.color, cfg);
      case 'line':  return renderLine(data, widget.color, cfg);
      case 'bar':   return renderBar(data, widget.color, cfg);
      case 'pie':   return renderPie(data, cfg);
      case 'table': return renderTable(data, cfg);
      case 'text':  return renderText(data);
      default:      return null;
    }
  }

  return (
    <div
      className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm flex flex-col gap-3"
      style={{ gridColumn: `span ${widget.colSpan}` }}
    >
      {/* Header */}
      <div className="flex items-start justify-between gap-2">
        <span className="text-sm font-bold text-gray-700 leading-tight">{widget.title}</span>
        {widget.dataSource && (
          <DataSourceBadge source={widget.dataSource} />
        )}
      </div>

      {/* Body */}
      {renderBody()}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Widget grid skeleton
// ---------------------------------------------------------------------------
function WidgetGridSkeleton() {
  return (
    <div className="grid gap-4" style={{ gridTemplateColumns: 'repeat(12, minmax(0, 1fr))' }}>
      {Array.from({ length: 4 }).map((_, i) => (
        <div key={i} className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm animate-pulse" style={{ gridColumn: 'span 6' }}>
          <div className="mb-3 h-4 w-1/2 rounded bg-gray-200" />
          <div className="h-32 rounded bg-gray-100" />
        </div>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------
// ReportScreen page
// ---------------------------------------------------------------------------
export function ReportScreen() {
  const navigate      = useNavigate();
  const { menuSlug }  = useParams<{ menuSlug: string }>();
  const [searchParams, setSearchParams] = useSearchParams();

  const [menu,        setMenu]        = useState<MenuDetail | null>(null);
  const [menuLoading, setMenuLoading] = useState(true);
  const [menuError,   setMenuError]   = useState<string | null>(null);

  const [screen,        setScreen]        = useState<ScreenDetail | null>(null);
  const [screenLoading, setScreenLoading] = useState(false);
  const [screenError,   setScreenError]   = useState<string | null>(null);

  // Incremented to trigger a fresh data-fetch in all WidgetCards
  const [refreshKey, setRefreshKey] = useState(0);

  const selectedScreenId = searchParams.get('screenId') ?? '';

  // Load menu detail
  useEffect(() => {
    if (!menuSlug) return;
    setMenuLoading(true);
    setMenuError(null);
    apiGet<MenuDetail>('/api/v1/reports/menus/' + menuSlug)
      .then((data) => {
        setMenu(data);
        if (!searchParams.get('screenId') && data.screens.length > 0) {
          setSearchParams({ screenId: data.screens[0].id }, { replace: true });
        }
      })
      .catch((err: unknown) => setMenuError(err instanceof Error ? err.message : 'Không thể tải menu'))
      .finally(() => setMenuLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [menuSlug]);

  // Load screen detail (widgets)
  useEffect(() => {
    if (!menuSlug || !selectedScreenId) return;
    setScreenLoading(true);
    setScreenError(null);
    setScreen(null);
    setRefreshKey(0); // reset on screen change
    apiGet<ScreenDetail>('/api/v1/reports/menus/' + menuSlug + '/screens/' + selectedScreenId)
      .then((data) => setScreen(data))
      .catch((err: unknown) => setScreenError(err instanceof Error ? err.message : 'Không thể tải màn hình'))
      .finally(() => setScreenLoading(false));
  }, [menuSlug, selectedScreenId]);

  // ── Timer-based auto-refresh ─────────────────────────────────────────────
  useEffect(() => {
    if (!screen || screen.refreshMode !== 'timer') return;
    const ms = (screen.refreshIntervalS > 0 ? screen.refreshIntervalS : 30) * 1000;
    const id = setInterval(() => setRefreshKey(k => k + 1), ms);
    return () => clearInterval(id);
  }, [screen?.refreshMode, screen?.refreshIntervalS, screen?.screenId]);

  // ── SSE-triggered refresh ────────────────────────────────────────────────
  useEffect(() => {
    if (!screen || screen.refreshMode !== 'sse') return;
    const channel = `screen:${screen.screenId}`;
    sseClient.subscribeWidget(channel);
    const unsub = sseClient.on('WidgetStale', (evt) => {
      if (evt.channel === channel) setRefreshKey(k => k + 1);
    });
    return () => {
      unsub();
      sseClient.unsubscribeWidget(channel);
    };
  }, [screen?.refreshMode, screen?.screenId]);

  function selectScreen(id: string) { setSearchParams({ screenId: id }); }

  // ─── render ───────────────────────────────────────────────────────────────

  if (menuLoading) {
    return (
      <div className="flex h-full items-center justify-center p-12">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-brand-600 border-t-transparent" />
      </div>
    );
  }

  if (menuError) {
    return (
      <div className="p-6">
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-red-700">
          <p className="font-semibold">Lỗi tải menu</p>
          <p className="mt-1 text-sm">{menuError}</p>
        </div>
      </div>
    );
  }

  if (!menu) return null;

  const sortedWidgets = screen
    ? [...screen.widgets].sort((a, b) => a.sortOrder - b.sortOrder)
    : [];

  return (
    <div className="flex h-full overflow-hidden">
      {/* ── Sidebar ── */}
      <aside className="flex w-56 shrink-0 flex-col border-r border-gray-200 bg-gray-50">
        <div className="flex items-center gap-2 border-b border-gray-200 px-4 py-4">
          <span className="text-xl">{menu.icon}</span>
          <span className="text-sm font-bold text-gray-800 leading-tight">{menu.name}</span>
        </div>
        <button
          type="button"
          onClick={() => navigate('/reports')}
          className="flex items-center gap-1.5 px-4 py-2 text-xs text-gray-400 hover:text-brand-600 transition-colors"
        >
          ← Tất cả báo cáo
        </button>
        <nav className="flex-1 overflow-y-auto py-2">
          {menu.screens.length === 0 && (
            <p className="px-4 py-3 text-xs text-gray-400">Không có màn hình nào</p>
          )}
          {menu.screens
            .slice()
            .sort((a, b) => a.sortOrder - b.sortOrder)
            .map((s) => {
              const active = s.id === selectedScreenId;
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => selectScreen(s.id)}
                  className={[
                    'flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors text-left',
                    active
                      ? 'border-l-2 border-brand-600 bg-brand-50 text-brand-700 font-semibold'
                      : 'border-l-2 border-transparent text-gray-600 hover:bg-gray-100',
                  ].join(' ')}
                >
                  <span className="text-base leading-none">{s.icon}</span>
                  <span className="leading-tight">{s.name}</span>
                </button>
              );
            })}
        </nav>
      </aside>

      {/* ── Main area ── */}
      <main className="flex flex-1 flex-col overflow-auto">
        {/* Screen header */}
        <div className="border-b border-gray-200 bg-white px-6 py-4">
          {screen ? (
            <div className="flex items-center gap-3">
              <span className="text-xl">{screen.icon}</span>
              <h2 className="text-xl font-bold text-gray-800">{screen.name}</h2>
              {/* Refresh mode badge */}
              {screen.refreshMode === 'timer' && screen.refreshIntervalS > 0 && (
                <span className="ml-1 flex items-center gap-1 rounded-full bg-blue-50 px-2.5 py-1 text-[10px] font-medium text-blue-600 border border-blue-100">
                  <span className="inline-block animate-spin" style={{ animationDuration: '3s' }}>⟳</span>
                  {screen.refreshIntervalS}s
                </span>
              )}
              {screen.refreshMode === 'sse' && (
                <span className="ml-1 flex items-center gap-1.5 rounded-full bg-green-50 px-2.5 py-1 text-[10px] font-medium text-green-600 border border-green-100">
                  <span className="inline-block h-1.5 w-1.5 rounded-full bg-green-500 animate-pulse" />
                  Live
                  {/* Manual trigger — hữu ích khi test hoặc gọi thủ công */}
                  <button
                    type="button"
                    title="Gửi WidgetStale để test SSE refresh"
                    onClick={() => {
                      void apiPost(`/api/v1/reports/screens/${screen.screenId}/stale`, {})
                        .catch(() => { /* silent */ });
                    }}
                    className="ml-0.5 rounded px-1 hover:bg-green-100 transition-colors text-green-500"
                  >
                    ⟳
                  </button>
                </span>
              )}
              {refreshKey > 0 && (
                <span className="text-[10px] text-gray-300 tabular-nums">#{refreshKey}</span>
              )}
            </div>
          ) : (
            <div className="h-7 w-48 animate-pulse rounded bg-gray-200" />
          )}
        </div>

        {/* Widget area */}
        <div className="flex-1 p-6">
          {screenLoading && <WidgetGridSkeleton />}

          {!screenLoading && screenError && (
            <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-red-700">
              <p className="font-semibold">Lỗi tải màn hình</p>
              <p className="mt-1 text-sm">{screenError}</p>
            </div>
          )}

          {!screenLoading && !screenError && screen && sortedWidgets.length === 0 && (
            <div className="rounded-xl border border-gray-200 bg-white p-10 text-center shadow-sm">
              <p className="text-gray-400">Màn hình này chưa có widget nào.</p>
            </div>
          )}

          {!screenLoading && !screenError && screen && sortedWidgets.length > 0 && (
            <div className="grid gap-4" style={{ gridTemplateColumns: 'repeat(12, minmax(0, 1fr))' }}>
              {sortedWidgets.map((w) => (
                <WidgetCard key={w.id} widget={w} refreshKey={refreshKey} />
              ))}
            </div>
          )}

          {!screenLoading && !screen && !screenError && !selectedScreenId && (
            <div className="rounded-xl border border-gray-200 bg-white p-10 text-center shadow-sm">
              <p className="text-gray-400">Chọn một màn hình từ danh sách bên trái.</p>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
