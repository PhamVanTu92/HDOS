import { useState, useCallback } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult, mapPushToResult } from '../../api/requests';
import { useSignalREvent } from '../../hooks/useSignalR';
import { listProviders } from '../../api/admin';
import { hasRealmRole } from '../../api/client';
import type { RequestResult } from '../../types/contracts';

// ── Types ─────────────────────────────────────────────────────────────────────

type ChartType = 'composite' | 'line' | 'bar' | 'pie' | 'table' | 'kpi' | 'heatmap';

interface WidgetDef {
  id:           string; // local key for React
  widgetId:     string;
  chartType:    ChartType;
  datasourceId: string;
  operation:    string;
  subscribesTo: string[];
}

interface DashboardDef {
  dashboardCode: string;
  title:         string;
  widgets:       WidgetDef[];
}

const CHART_TYPES: ChartType[] = ['composite', 'line', 'bar', 'pie', 'table', 'kpi', 'heatmap'];

const KNOWN_OPERATIONS = [
  'report.dashboard.summary',
  'report.sales.trend',
  'report.inventory.status',
  'report.regional.performance',
  'report.channel.comparison',
  'report.product.detail',
  'report.top.performers',
];

const KNOWN_EVENTS = ['datasource.updated', 'schema.changed', 'cache.invalidated'];

function newWidget(): WidgetDef {
  return {
    id:           Math.random().toString(36).slice(2),
    widgetId:     '',
    chartType:    'line',
    datasourceId: '',
    operation:    '',
    subscribesTo: ['datasource.updated'],
  };
}

function toApiDefinition(def: DashboardDef) {
  return {
    dashboardCode: def.dashboardCode,
    title:         def.title,
    widgets: def.widgets.map(({ widgetId, chartType, datasourceId, operation, subscribesTo }) => ({
      widgetId,
      chartType,
      datasourceId,
      operation,
      subscribesTo,
    })),
  };
}

// ── Icons ─────────────────────────────────────────────────────────────────────

function XIcon({ className = 'h-4 w-4' }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
    </svg>
  );
}

function PlusIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
    </svg>
  );
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg className={`h-4 w-4 transition-transform ${open ? 'rotate-180' : ''}`}
      fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
    </svg>
  );
}

function SpinnerIcon() {
  return (
    <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth={4} />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
    </svg>
  );
}

// ── Widget Row Editor ─────────────────────────────────────────────────────────

interface WidgetEditorProps {
  widget:    WidgetDef;
  index:     number;
  providers: string[];
  onChange:  (w: WidgetDef) => void;
  onRemove:  () => void;
}

function WidgetEditor({ widget, index, providers, onChange, onRemove }: WidgetEditorProps) {
  const [open, setOpen] = useState(true);

  function set<K extends keyof WidgetDef>(k: K, v: WidgetDef[K]) {
    onChange({ ...widget, [k]: v });
  }

  function toggleEvent(event: string) {
    const next = widget.subscribesTo.includes(event)
      ? widget.subscribesTo.filter((e) => e !== event)
      : [...widget.subscribesTo, event];
    set('subscribesTo', next);
  }

  function addCustomEvent(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key !== 'Enter') return;
    const val = (e.currentTarget.value ?? '').trim();
    if (!val || widget.subscribesTo.includes(val)) return;
    set('subscribesTo', [...widget.subscribesTo, val]);
    e.currentTarget.value = '';
  }

  return (
    <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
      {/* Widget header */}
      <div
        className="flex cursor-pointer items-center gap-3 px-5 py-3"
        onClick={() => setOpen(!open)}
      >
        <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-brand-100 text-xs font-bold text-brand-700">
          {index + 1}
        </span>
        <span className="flex-1 font-medium text-gray-800 text-sm">
          {widget.widgetId || <span className="text-gray-400 italic">Widget {index + 1}</span>}
        </span>
        <span className="text-xs text-gray-400 font-mono">{widget.chartType}</span>
        <button
          onClick={(e) => { e.stopPropagation(); onRemove(); }}
          className="ml-2 rounded p-1 text-gray-400 hover:bg-red-50 hover:text-red-500"
          title="Xóa widget"
        >
          <XIcon />
        </button>
        <ChevronIcon open={open} />
      </div>

      {open && (
        <div className="border-t border-gray-100 px-5 py-4 space-y-4">
          {/* Row 1: widgetId + chartType */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Widget ID <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                value={widget.widgetId}
                onChange={(e) => set('widgetId', e.target.value)}
                placeholder="sales-chart"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Chart Type</label>
              <select
                value={widget.chartType}
                onChange={(e) => set('chartType', e.target.value as ChartType)}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              >
                {CHART_TYPES.map((ct) => (
                  <option key={ct} value={ct}>{ct}</option>
                ))}
              </select>
            </div>
          </div>

          {/* Row 2: datasourceId + operation */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Datasource ID</label>
              <input
                type="text"
                value={widget.datasourceId}
                onChange={(e) => set('datasourceId', e.target.value)}
                placeholder="excel-provider"
                list={`ds-list-${widget.id}`}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
              <datalist id={`ds-list-${widget.id}`}>
                {providers.map((p) => <option key={p} value={p} />)}
              </datalist>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Operation</label>
              <input
                type="text"
                value={widget.operation}
                onChange={(e) => set('operation', e.target.value)}
                placeholder="report.sales.trend"
                list={`op-list-${widget.id}`}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
              <datalist id={`op-list-${widget.id}`}>
                {KNOWN_OPERATIONS.map((op) => <option key={op} value={op} />)}
              </datalist>
            </div>
          </div>

          {/* Row 3: subscribesTo */}
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-2">
              Subscribes To <span className="text-gray-400 font-normal">(sự kiện tự refresh)</span>
            </label>
            <div className="flex flex-wrap gap-2">
              {KNOWN_EVENTS.map((evt) => (
                <label key={evt} className="flex items-center gap-1.5 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={widget.subscribesTo.includes(evt)}
                    onChange={() => toggleEvent(evt)}
                    className="rounded border-gray-300 text-brand-600"
                  />
                  <span className="text-xs font-mono text-gray-700">{evt}</span>
                </label>
              ))}
            </div>
            {/* Custom events */}
            {widget.subscribesTo.filter((e) => !KNOWN_EVENTS.includes(e)).map((e) => (
              <div key={e} className="mt-1.5 flex items-center gap-1">
                <span className="rounded bg-purple-100 px-2 py-0.5 font-mono text-xs text-purple-700">
                  {e}
                </span>
                <button
                  onClick={() => set('subscribesTo', widget.subscribesTo.filter((x) => x !== e))}
                  className="text-gray-400 hover:text-red-500"
                >
                  <XIcon className="h-3 w-3" />
                </button>
              </div>
            ))}
            <input
              type="text"
              placeholder="Nhập custom event rồi Enter…"
              onKeyDown={addCustomEvent}
              className="mt-2 w-full rounded-lg border border-dashed border-gray-300 px-3 py-1.5 text-xs font-mono text-gray-500 focus:border-brand-400 outline-none"
            />
          </div>
        </div>
      )}
    </div>
  );
}

// ── Main Component ────────────────────────────────────────────────────────────

export function ReportDesigner() {
  const auth     = useAuth();
  const userId   = auth.user?.profile.sub ?? '';
  const tenantId = (auth.user?.profile['tenant_id'] as string | undefined) ?? userId;

  const [def, setDef] = useState<DashboardDef>({
    dashboardCode: '',
    title:         '',
    widgets:       [],
  });

  const [showPreview, setShowPreview] = useState(false);

  // Submit state
  const [requestId,   setRequestId]   = useState<string | null>(null);
  const [submitting,  setSubmitting]  = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [pushed,      setPushed]      = useState<RequestResult<unknown> | null>(null);

  // Load provider list for datasource suggestions
  const { data: providers } = useQuery({
    queryKey: ['admin-providers'],
    queryFn:  listProviders,
    select:   (ps) => ps.map((p) => p.providerId),
    staleTime: 60_000,
  });

  // ── SignalR listeners ───────────────────────────────────────────────────────

  useSignalREvent('RequestCompleted', (payload) => {
    if (payload.requestId === requestId) setPushed(mapPushToResult(payload));
  }, true);

  useSignalREvent('RequestFailed', (payload) => {
    if (payload.requestId === requestId) setPushed(mapPushToResult(payload));
  }, true);

  // ── Fallback polling ────────────────────────────────────────────────────────

  const { data: polled, isLoading: resultLoading } = useQuery<RequestResult<unknown>>({
    queryKey: ['report-designer-result', requestId],
    queryFn:  () => getRequestResult<unknown>(requestId!),
    enabled:  !!requestId && !pushed,
    retry: false,
    refetchInterval: (query) => {
      if (pushed) return false;
      if (query.state.error) return false;
      const s = query.state.data?.status;
      if (!s) return 3000;
      return ['Completed', 'Failed', 'Cancelled'].includes(s) ? false : 3000;
    },
    staleTime: 0,
  });

  const result    = pushed ?? polled ?? null;
  const isRunning = submitting || (!!requestId && !result);

  // ── Handlers ────────────────────────────────────────────────────────────────

  function addWidget() {
    setDef((d) => ({ ...d, widgets: [...d.widgets, newWidget()] }));
  }

  function updateWidget(id: string, w: WidgetDef) {
    setDef((d) => ({ ...d, widgets: d.widgets.map((x) => (x.id === id ? w : x)) }));
  }

  function removeWidget(id: string) {
    setDef((d) => ({ ...d, widgets: d.widgets.filter((x) => x.id !== id) }));
  }

  const handleSubmit = useCallback(async () => {
    if (submitting) return;
    setSubmitting(true);
    setSubmitError(null);
    setPushed(null);
    setRequestId(null);

    try {
      const ack = await submitRequest({
        operation:    'metadata.dashboards.upsert',
        params:       { Definition: toApiDefinition(def) },
        tenantId,
        userId,
        priority:     'Normal',
        cacheSeconds: 0,
      });
      setRequestId(ack.requestId);
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }, [submitting, def, tenantId, userId]);

  const isValid = def.dashboardCode.trim() && def.title.trim() &&
    def.widgets.length > 0 &&
    def.widgets.every((w) => w.widgetId && w.operation);

  if (!hasRealmRole('admin')) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-red-100">
            <XIcon className="h-8 w-8 text-red-500" />
          </div>
          <h2 className="text-xl font-semibold text-gray-900">Không có quyền truy cập</h2>
          <p className="mt-2 text-gray-500">Bạn cần vai trò <code>admin</code> để xem trang này.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Thiết kế Báo cáo</h1>
        <p className="mt-1 text-sm text-gray-500">
          Xây dựng cấu hình dashboard và widgets, tự động đăng ký với hệ thống qua{' '}
          <code className="font-mono text-xs bg-gray-100 px-1 rounded">metadata.dashboards.upsert</code>.
        </p>
      </div>

      <div className="space-y-5">
        {/* Dashboard info */}
        <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-5">
          <h2 className="font-semibold text-gray-800 mb-4">Thông tin Dashboard</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Dashboard Code <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                value={def.dashboardCode}
                onChange={(e) => setDef((d) => ({ ...d, dashboardCode: e.target.value }))}
                placeholder="main-dashboard"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Tiêu đề <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                value={def.title}
                onChange={(e) => setDef((d) => ({ ...d, title: e.target.value }))}
                placeholder="Main Dashboard"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>
          </div>
        </div>

        {/* Widgets */}
        <div>
          <div className="flex items-center justify-between mb-3">
            <div>
              <h2 className="font-semibold text-gray-800">
                Widgets{' '}
                <span className="ml-1 text-sm font-normal text-gray-400">
                  ({def.widgets.length})
                </span>
              </h2>
              <p className="text-xs text-gray-500 mt-0.5">
                Mỗi widget hiển thị dữ liệu từ một operation và tự refresh khi nhận sự kiện.
              </p>
            </div>
            <button
              onClick={addWidget}
              className="flex items-center gap-1.5 rounded-lg bg-brand-600 px-3 py-2 text-sm font-medium text-white hover:bg-brand-700"
            >
              <PlusIcon />
              Thêm widget
            </button>
          </div>

          {def.widgets.length === 0 ? (
            <div
              onClick={addWidget}
              className="flex cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed border-gray-200 py-12 text-center hover:border-brand-300 hover:bg-brand-50 transition-colors"
            >
              <PlusIcon />
              <p className="mt-2 text-sm font-medium text-gray-500">Thêm widget đầu tiên</p>
              <p className="text-xs text-gray-400">Click để bắt đầu thiết kế</p>
            </div>
          ) : (
            <div className="space-y-3">
              {def.widgets.map((w, i) => (
                <WidgetEditor
                  key={w.id}
                  widget={w}
                  index={i}
                  providers={providers ?? []}
                  onChange={(updated) => updateWidget(w.id, updated)}
                  onRemove={() => removeWidget(w.id)}
                />
              ))}
            </div>
          )}
        </div>

        {/* Preview toggle */}
        {def.widgets.length > 0 && (
          <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
            <button
              onClick={() => setShowPreview(!showPreview)}
              className="flex w-full items-center justify-between px-5 py-3"
            >
              <span className="font-semibold text-gray-800 text-sm">Xem trước JSON</span>
              <ChevronIcon open={showPreview} />
            </button>
            {showPreview && (
              <div className="border-t border-gray-100 px-5 py-4">
                <pre className="text-xs font-mono bg-gray-50 rounded-lg p-4 overflow-auto max-h-80 text-gray-700">
                  {JSON.stringify(toApiDefinition(def), null, 2)}
                </pre>
              </div>
            )}
          </div>
        )}

        {/* Submit + Result */}
        <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-5 space-y-4">
          {submitError && (
            <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              {submitError}
            </div>
          )}

          <button
            onClick={handleSubmit}
            disabled={isRunning || !isValid}
            className="flex w-full items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-3 text-sm font-semibold text-white hover:bg-brand-700 disabled:opacity-60"
          >
            {isRunning && <SpinnerIcon />}
            {submitting ? 'Đang gửi…' : isRunning ? 'Đang xử lý…' : 'Lưu Dashboard Definition'}
          </button>

          {!isValid && !isRunning && (
            <p className="text-xs text-gray-400 text-center">
              Điền đầy đủ Dashboard Code, Tiêu đề và ít nhất 1 widget có Widget ID và Operation.
            </p>
          )}

          {/* Result */}
          {result && (
            <div className={`rounded-lg border p-4 ${
              result.status === 'Completed'
                ? 'border-green-200 bg-green-50'
                : result.status === 'Failed'
                ? 'border-red-200 bg-red-50'
                : 'border-gray-200 bg-gray-50'
            }`}>
              <div className="flex items-center gap-2 mb-2">
                <span className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                  result.status === 'Completed' ? 'bg-green-100 text-green-800' :
                  result.status === 'Failed' ? 'bg-red-100 text-red-800' : 'bg-gray-100 text-gray-600'
                }`}>{result.status}</span>
                {result.status === 'Completed' && (
                  <span className="text-xs text-green-700 font-medium">
                    Dashboard đã được đăng ký thành công!
                  </span>
                )}
                {result.error && (
                  <span className="text-xs text-red-600">{result.error}</span>
                )}
                {!pushed && resultLoading && (
                  <span className="flex items-center gap-1 text-xs text-gray-400">
                    <SpinnerIcon />polling…
                  </span>
                )}
              </div>
              {result.data != null && (
                <pre className="text-xs font-mono text-gray-600 overflow-auto max-h-40 whitespace-pre-wrap">
                  {JSON.stringify(result.data, null, 2)}
                </pre>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
