import { useState } from 'react';
import { useReport } from '../hooks/useReport';
import { ReportForm } from '../components/reports/ReportForm';
import { ReportResult } from '../components/reports/ReportResult';
import { ProgressBar } from '../components/reports/ProgressBar';
import type { ReportOperation, RequestStatus } from '../types/contracts';

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

  const handleSubmit = (
    operation: ReportOperation,
    params: Record<string, unknown>,
  ) => {
    setCurrentOperation(operation);
    submit(operation, params);
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-gray-800">Reports</h1>
        <p className="text-sm text-gray-500">
          Submit analytical reports and track their progress in real-time.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Form panel */}
        <div className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-700">
            Report Parameters
          </h2>
          <ReportForm onSubmit={handleSubmit} isRunning={isRunning} />
        </div>

        {/* Result panel */}
        <div className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm lg:col-span-2">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-gray-700">Result</h2>
            {(data || error) && (
              <button
                onClick={reset}
                className="text-xs text-brand-600 hover:underline"
              >
                Clear
              </button>
            )}
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
            <div className="flex h-64 items-center justify-center text-sm text-gray-400">
              Select a report type and click Run Report to get started.
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
