import { useState, useCallback, useRef, useEffect } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult, mapPushToResult } from '../../api/requests';
import { useSignalREvent } from '../../hooks/useSignalR';
import { hasRealmRole } from '../../api/client';
import type {
  RequestResult,
  RequestCompletedEvent,
  RequestFailedEvent,
  WidgetStaleEvent,
} from '../../types/contracts';

// ── Known operations with sample params ──────────────────────────────────────

const SAMPLE_PARAMS: Record<string, string> = {
  'report.dashboard.summary':    '{}',
  'report.sales.trend':          '{\n  "fromDate": "2025-01-01",\n  "toDate": "2025-12-31",\n  "groupBy": "day"\n}',
  'report.regional.performance': '{\n  "period": "today"\n}',
  'report.inventory.status':     '{}',
  'report.channel.comparison':   '{}',
  'report.top.performers':       '{}',
  'report.product.detail':       '{\n  "productName": ""\n}',
  'metadata.dashboards.upsert':  '{\n  "Definition": {\n    "dashboardCode": "my-dashboard",\n    "title": "My Dashboard",\n    "widgets": []\n  }\n}',
};

// ── Event log ─────────────────────────────────────────────────────────────────

interface LogEntry {
  id:        string;
  ts:        Date;
  type:      'RequestCompleted' | 'RequestFailed' | 'WidgetStale' | 'Submitted';
  payload:   unknown;
  isCurrent: boolean;
}

function logEntryColors(entry: LogEntry) {
  if (entry.isCurrent && entry.type === 'RequestCompleted')
    return 'border-green-200 bg-green-50';
  if (entry.isCurrent && entry.type === 'RequestFailed')
    return 'border-red-200 bg-red-50';
  if (entry.isCurrent && entry.type === 'Submitted')
    return 'border-brand-200 bg-brand-50';
  if (entry.type === 'WidgetStale')
    return 'border-yellow-100 bg-yellow-50';
  if (entry.type === 'RequestFailed')
    return 'border-red-100 bg-red-50';
  if (entry.type === 'RequestCompleted')
    return 'border-gray-100 bg-gray-50';
  return 'border-gray-100 bg-white';
}

function logBadgeColors(type: LogEntry['type']) {
  switch (type) {
    case 'RequestCompleted': return 'bg-green-100 text-green-700';
    case 'RequestFailed':    return 'bg-red-100 text-red-700';
    case 'WidgetStale':      return 'bg-yellow-100 text-yellow-700';
    case 'Submitted':        return 'bg-brand-100 text-brand-700';
  }
}

// ── Icons ─────────────────────────────────────────────────────────────────────

function XIcon({ className = 'h-4 w-4' }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
    </svg>
  );
}

function PlayIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
    </svg>
  );
}

function SpinnerIcon() {
  return (
    <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth={4} />
      <path className="opacity-75" fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
    </svg>
  );
}

// ── Main Component ────────────────────────────────────────────────────────────

export function TestConsole() {
  const auth     = useAuth();
  const userId   = auth.user?.profile.sub ?? '';
  const tenantId = (auth.user?.profile['tenant_id'] as string | undefined) ?? userId;

  // Form state
  const [operation,    setOperation]    = useState('report.dashboard.summary');
  const [paramsText,   setParamsText]   = useState('{}');
  const [priority,     setPriority]     = useState<'Low' | 'Normal' | 'High'>('Normal');
  const [cacheSeconds, setCacheSeconds] = useState(60);
  const [paramsError,  setParamsError]  = useState<string | null>(null);

  // Request lifecycle
  const [requestId,   setRequestId]   = useState<string | null>(null);
  const [submitting,  setSubmitting]  = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [pushed,      setPushed]      = useState<RequestResult<unknown> | null>(null);

  // Use a ref so SignalR handlers always see the latest requestId
  const requestIdRef = useRef<string | null>(null);
  requestIdRef.current = requestId;

  // Event log
  const [log, setLog] = useState<LogEntry[]>([]);
  const logEndRef     = useRef<HTMLDivElement>(null);

  // Auto-scroll log to bottom
  useEffect(() => {
    logEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [log]);

  const appendLog = useCallback((entry: Omit<LogEntry, 'id' | 'ts'>) => {
    setLog((prev) => [
      ...prev.slice(-299), // keep last 300 entries
      { ...entry, id: Math.random().toString(36).slice(2), ts: new Date() },
    ]);
  }, []);

  // ── Submit ──────────────────────────────────────────────────────────────────

  const handleSubmit = useCallback(async () => {
    if (submitting) return;

    let params: Record<string, unknown> = {};
    try {
      if (paramsText.trim()) params = JSON.parse(paramsText) as Record<string, unknown>;
      setParamsError(null);
    } catch (e) {
      setParamsError(`JSON không hợp lệ: ${e instanceof Error ? e.message : String(e)}`);
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    setPushed(null);
    setRequestId(null);

    try {
      const ack = await submitRequest({ operation, params, tenantId, userId, priority, cacheSeconds });
      setRequestId(ack.requestId);
      appendLog({ type: 'Submitted', isCurrent: true, payload: { requestId: ack.requestId, operation, params } });
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }, [submitting, operation, paramsText, tenantId, userId, priority, cacheSeconds, appendLog]);

  // ── SignalR handlers (always registered — handlerRef keeps them fresh) ──────

  useSignalREvent(
    'RequestCompleted',
    (payload: RequestCompletedEvent) => {
      const isCurrent = payload.requestId === requestIdRef.current;
      if (isCurrent) setPushed(mapPushToResult(payload));
      appendLog({ type: 'RequestCompleted', isCurrent, payload });
    },
    true,
  );

  useSignalREvent(
    'RequestFailed',
    (payload: RequestFailedEvent) => {
      const isCurrent = payload.requestId === requestIdRef.current;
      if (isCurrent) setPushed(mapPushToResult(payload));
      appendLog({ type: 'RequestFailed', isCurrent, payload });
    },
    true,
  );

  useSignalREvent(
    'WidgetStale',
    (payload: WidgetStaleEvent) => {
      appendLog({ type: 'WidgetStale', isCurrent: false, payload });
    },
    true,
  );

  // ── Fallback polling ────────────────────────────────────────────────────────

  const { data: polled, isLoading: resultLoading } = useQuery<RequestResult<unknown>>({
    queryKey: ['test-console-result', requestId],
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
  const status    = result?.status ?? (submitting ? 'Queued' : null);

  const pct = status === 'Queued' ? 20 : status === 'Processing' ? 60 : status ? 100 : 0;

  // ── Operation picker helper ─────────────────────────────────────────────────

  function pickOperation(op: string) {
    setOperation(op);
    if (SAMPLE_PARAMS[op]) setParamsText(SAMPLE_PARAMS[op]);
    setParamsError(null);
    setPushed(null);
    setRequestId(null);
  }

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
    <div className="max-w-7xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Test Console</h1>
        <p className="mt-1 text-sm text-gray-500">
          Gửi yêu cầu thủ công và theo dõi kết quả theo thời gian thực qua SignalR.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">

        {/* ── Left panel: Form + Result ──────────────────────────────────────── */}
        <div className="space-y-4">

          {/* Quick-pick operations */}
          <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-4">
            <p className="text-xs font-medium text-gray-500 mb-2 uppercase tracking-wide">
              Chọn nhanh
            </p>
            <div className="flex flex-wrap gap-1.5">
              {Object.keys(SAMPLE_PARAMS).map((op) => (
                <button
                  key={op}
                  onClick={() => pickOperation(op)}
                  className={`rounded-full px-3 py-1 text-xs font-mono transition-colors ${
                    operation === op
                      ? 'bg-brand-600 text-white'
                      : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                  }`}
                >
                  {op}
                </button>
              ))}
            </div>
          </div>

          {/* Form */}
          <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-5 space-y-4">
            <h2 className="font-semibold text-gray-800">Yêu cầu</h2>

            {/* Operation */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Operation</label>
              <input
                type="text"
                value={operation}
                onChange={(e) => pickOperation(e.target.value)}
                placeholder="report.dashboard.summary"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>

            {/* Params */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Params <span className="font-normal text-gray-400">(JSON)</span>
              </label>
              <textarea
                value={paramsText}
                onChange={(e) => { setParamsText(e.target.value); setParamsError(null); }}
                rows={7}
                spellCheck={false}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none resize-none"
              />
              {paramsError && (
                <p className="mt-1 text-xs text-red-600">{paramsError}</p>
              )}
            </div>

            {/* Priority + Cache */}
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Priority</label>
                <select
                  value={priority}
                  onChange={(e) => setPriority(e.target.value as typeof priority)}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
                >
                  <option>Low</option>
                  <option>Normal</option>
                  <option>High</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Cache (giây)</label>
                <input
                  type="number"
                  value={cacheSeconds}
                  onChange={(e) => setCacheSeconds(parseInt(e.target.value) || 0)}
                  min={0} max={3600}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
                />
              </div>
            </div>

            {submitError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                {submitError}
              </div>
            )}

            <button
              onClick={handleSubmit}
              disabled={isRunning || !operation.trim()}
              className="flex w-full items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-60"
            >
              {isRunning ? <SpinnerIcon /> : <PlayIcon />}
              {submitting ? 'Đang gửi…' : isRunning ? 'Đang xử lý…' : 'Gửi yêu cầu'}
            </button>
          </div>

          {/* Result */}
          {(requestId || result) && (
            <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-5 space-y-3">
              <div className="flex items-center justify-between">
                <h2 className="font-semibold text-gray-800">Kết quả</h2>
                {result?.completedAt && (
                  <span className="text-xs text-gray-400">
                    {new Date(result.completedAt).toLocaleTimeString()}
                  </span>
                )}
              </div>

              {/* Progress bar */}
              {(isRunning || status) && (
                <div>
                  <div className="flex justify-between text-xs text-gray-500 mb-1">
                    <span className="font-medium">{status ?? 'Queued'}</span>
                    {requestId && (
                      <code className="text-gray-400">{requestId.slice(0, 8)}…</code>
                    )}
                  </div>
                  <div className="h-1.5 rounded-full bg-gray-200 overflow-hidden">
                    <div
                      className={`h-1.5 rounded-full transition-all duration-700 ${
                        result?.status === 'Completed' ? 'bg-green-500' :
                        result?.status === 'Failed'    ? 'bg-red-500' :
                        'bg-brand-500 animate-pulse'
                      }`}
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                </div>
              )}

              {/* Status badge */}
              {result && (
                <div className="flex items-center gap-2 flex-wrap">
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                    result.status === 'Completed' ? 'bg-green-100 text-green-800' :
                    result.status === 'Failed'    ? 'bg-red-100 text-red-800' :
                    result.status === 'Cancelled' ? 'bg-gray-100 text-gray-600' :
                    'bg-blue-100 text-blue-800'
                  }`}>{result.status}</span>
                  {result.error && (
                    <span className="text-xs text-red-600">{result.error}</span>
                  )}
                  {!pushed && resultLoading && (
                    <span className="text-xs text-gray-400 flex items-center gap-1">
                      <SpinnerIcon />polling…
                    </span>
                  )}
                </div>
              )}

              {/* Data */}
              {result?.data != null && (
                <pre className="text-xs font-mono bg-gray-50 border border-gray-200 rounded-lg p-3 overflow-auto max-h-72 text-gray-700 whitespace-pre-wrap break-all">
                  {JSON.stringify(result.data, null, 2)}
                </pre>
              )}
            </div>
          )}
        </div>

        {/* ── Right panel: Live event log ────────────────────────────────────── */}
        <div className="flex flex-col rounded-xl border border-gray-200 bg-white shadow-sm">
          {/* Log header */}
          <div className="flex items-center justify-between border-b border-gray-100 px-5 py-4">
            <div>
              <h2 className="font-semibold text-gray-800">Luồng sự kiện SignalR</h2>
              <p className="text-xs text-gray-400 mt-0.5">
                RequestCompleted · RequestFailed · WidgetStale
              </p>
            </div>
            <div className="flex items-center gap-2">
              <span className="flex h-2 w-2 items-center justify-center">
                <span className="animate-ping absolute inline-flex h-2 w-2 rounded-full bg-green-400 opacity-75" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-green-500" />
              </span>
              <button
                onClick={() => setLog([])}
                className="rounded-md border border-gray-200 px-2 py-1 text-xs text-gray-500 hover:bg-gray-50"
              >
                Xóa log
              </button>
            </div>
          </div>

          {/* Log body */}
          <div className="flex-1 overflow-y-auto p-3 space-y-2 min-h-96" style={{ maxHeight: '70vh' }}>
            {log.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full py-16 text-gray-300">
                <svg className="h-10 w-10 mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                    d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0" />
                </svg>
                <p className="text-sm">Chờ sự kiện SignalR…</p>
                <p className="text-xs mt-1">Gửi một yêu cầu hoặc chờ provider push dữ liệu.</p>
              </div>
            ) : (
              log.map((entry) => (
                <div
                  key={entry.id}
                  className={`rounded-lg border p-2.5 font-mono text-xs ${logEntryColors(entry)}`}
                >
                  <div className="flex items-center gap-1.5 mb-1.5">
                    <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${logBadgeColors(entry.type)}`}>
                      {entry.type}
                    </span>
                    <span className="text-gray-400">{entry.ts.toLocaleTimeString()}</span>
                    {entry.isCurrent && (
                      <span className="ml-auto text-[10px] font-bold text-brand-600">● CURRENT</span>
                    )}
                  </div>
                  <pre className="text-gray-600 whitespace-pre-wrap break-all max-h-40 overflow-hidden">
                    {JSON.stringify(entry.payload, null, 2)}
                  </pre>
                </div>
              ))
            )}
            <div ref={logEndRef} />
          </div>
        </div>
      </div>
    </div>
  );
}
