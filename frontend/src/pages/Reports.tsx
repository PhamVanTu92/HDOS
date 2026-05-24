import { useState, useCallback, useRef } from 'react';
import { useReport } from '../hooks/useReport';
import { useWidgetSubscription } from '../hooks/useSignalR';
import { ReportForm } from '../components/reports/ReportForm';
import { ReportResult } from '../components/reports/ReportResult';
import { ProgressBar } from '../components/reports/ProgressBar';
import type { ReportOperation, RequestStatus, WidgetStaleEvent } from '../types/contracts';

// Same channel as Dashboard — WidgetStale fires here whenever excel-provider
// pushes a datasource.updated event that triggers the main-dashboard group.
const WIDGET_CHANNEL = 'widget:main-dashboard:main-dashboard';

export function Reports() {
  const [currentOperation, setCurrentOperation] =
    useState<ReportOperation>('report.dashboard.summary');

  const {
    submit,
    reset,
    status,
    progressPct,
    isRunning,
    data,
    error,
    requestId,
  } = useReport();

  // Track the last submitted operation + params so we can re-run them on
  // WidgetStale without needing to touch ReportForm's internal state.
  const lastSubmitRef = useRef<{
    operation: ReportOperation;
    params: Record<string, unknown>;
  } | null>(null);

  const [lastAutoRefresh, setLastAutoRefresh] = useState<Date | null>(null);
  const [autoRefreshCount, setAutoRefreshCount] = useState(0);

  const handleSubmit = (
    operation: ReportOperation,
    params: Record<string, unknown>,
  ) => {
    lastSubmitRef.current = { operation, params };
    setCurrentOperation(operation);
    submit(operation, params);
  };

  // ── Auto-refresh via WidgetStale ──────────────────────────────────────────
  // Fires whenever excel-provider pushes a datasource.updated event.
  // Re-runs the last viewed report with identical params so the result stays
  // current without any user interaction.
  const handleWidgetStale = useCallback(
    (_payload: WidgetStaleEvent) => {
      if (!lastSubmitRef.current) return; // no report run yet — ignore
      const { operation, params } = lastSubmitRef.current;
      submit(operation, params);
      setLastAutoRefresh(new Date());
      setAutoRefreshCount((n) => n + 1);
    },
    [submit],
  );

  // Always subscribed — picks up WidgetStale whether or not a report is open.
  useWidgetSubscription(WIDGET_CHANNEL, handleWidgetStale, true);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-gray-800">Báo cáo</h1>
          <p className="text-sm text-gray-500">
            Chạy báo cáo và theo dõi tiến trình theo thời gian thực.
          </p>
        </div>

        {/* Auto-refresh status badge */}
        <div className="flex-shrink-0 rounded-lg border border-green-200 bg-green-50 px-3 py-2 text-right">
          <div className="flex items-center gap-1.5">
            <span className="relative flex h-2 w-2">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75" />
              <span className="relative inline-flex h-2 w-2 rounded-full bg-green-500" />
            </span>
            <span className="text-xs font-medium text-green-700">
              Tự động làm mới khi dữ liệu thay đổi
            </span>
          </div>
          {lastAutoRefresh ? (
            <p className="mt-0.5 text-xs text-green-600">
              Lần cuối: {lastAutoRefresh.toLocaleTimeString()}
              {autoRefreshCount > 1 && ` (${autoRefreshCount} lần)`}
            </p>
          ) : (
            <p className="mt-0.5 text-xs text-green-500">
              {lastSubmitRef.current
                ? 'Chờ provider push dữ liệu…'
                : 'Chạy báo cáo để kích hoạt'}
            </p>
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Form panel */}
        <div className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-700">
            Tham số báo cáo
          </h2>
          <ReportForm onSubmit={handleSubmit} isRunning={isRunning} />
        </div>

        {/* Result panel */}
        <div className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm lg:col-span-2">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-gray-700">Kết quả</h2>
            <div className="flex items-center gap-3">
              {/* Auto-refresh chip — shows if last submit was auto-triggered */}
              {lastAutoRefresh && !isRunning && (
                <span className="inline-flex items-center gap-1 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">
                  <span className="h-1.5 w-1.5 rounded-full bg-green-500" />
                  Làm mới tự động
                </span>
              )}
              {(data || error) && (
                <button
                  onClick={() => {
                    reset();
                    lastSubmitRef.current = null;
                    setLastAutoRefresh(null);
                    setAutoRefreshCount(0);
                  }}
                  className="text-xs text-brand-600 hover:underline"
                >
                  Xóa
                </button>
              )}
            </div>
          </div>

          {/* Progress */}
          {status && (
            <div className="mb-4">
              <ProgressBar
                pct={progressPct}
                status={status as RequestStatus}
              />
            </div>
          )}

          {/* Error */}
          {error && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              {error}
            </div>
          )}

          {/* Result */}
          {data && requestId && (
            <ReportResult
              operation={currentOperation}
              data={data}
              requestId={requestId}
            />
          )}

          {/* Empty state */}
          {!status && !data && !error && (
            <div className="flex h-64 flex-col items-center justify-center gap-3 text-sm text-gray-400">
              <svg className="h-10 w-10 text-gray-200" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                  d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              <div className="text-center">
                <p>Chọn loại báo cáo và bấm <strong>Chạy báo cáo</strong>.</p>
                <p className="text-xs mt-1 text-gray-300">
                  Sau khi chạy, kết quả sẽ tự động làm mới khi provider cập nhật dữ liệu.
                </p>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
