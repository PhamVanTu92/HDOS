import { useState, type FormEvent } from 'react';
import { REPORT_TYPES, type ReportOperation, type GroupBy, type Period } from '../../types/contracts';

interface ReportFormProps {
  onSubmit: (operation: ReportOperation, params: Record<string, unknown>) => void;
  isRunning: boolean;
}

export function ReportForm({ onSubmit, isRunning }: ReportFormProps) {
  const [operation, setOperation] = useState<ReportOperation>(
    'report.dashboard.summary',
  );

  // Dashboard summary params
  const [date, setDate] = useState('');

  // Sales trend params
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [groupBy, setGroupBy] = useState<GroupBy>('day');

  // Regional performance params
  const [period, setPeriod] = useState<Period>('today');

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    let params: Record<string, unknown> = {};

    switch (operation) {
      case 'report.dashboard.summary':
        if (date) params = { date };
        break;
      case 'report.sales.trend':
        params = { fromDate, toDate, groupBy };
        break;
      case 'report.inventory.status':
        params = {};
        break;
      case 'report.regional.performance':
        params = { period };
        break;
    }

    onSubmit(operation, params);
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {/* Report type */}
      <div>
        <label className="mb-1 block text-sm font-medium text-gray-700">
          Report Type
        </label>
        <select
          value={operation}
          onChange={(e) => setOperation(e.target.value as ReportOperation)}
          className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
        >
          {REPORT_TYPES.map((r) => (
            <option key={r.operation} value={r.operation}>
              {r.label}
            </option>
          ))}
        </select>
        <p className="mt-1 text-xs text-gray-500">
          {REPORT_TYPES.find((r) => r.operation === operation)?.description}
        </p>
      </div>

      {/* Params by operation */}
      {operation === 'report.dashboard.summary' && (
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            Date (optional, defaults to today)
          </label>
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          />
        </div>
      )}

      {operation === 'report.sales.trend' && (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">
                From Date
              </label>
              <input
                type="date"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
                required
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">
                To Date
              </label>
              <input
                type="date"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
                required
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">
              Group By
            </label>
            <select
              value={groupBy}
              onChange={(e) => setGroupBy(e.target.value as GroupBy)}
              className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            >
              <option value="day">Day</option>
              <option value="week">Week</option>
              <option value="month">Month</option>
            </select>
          </div>
        </div>
      )}

      {operation === 'report.regional.performance' && (
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            Period
          </label>
          <select
            value={period}
            onChange={(e) => setPeriod(e.target.value as Period)}
            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          >
            <option value="today">Today</option>
            <option value="week">This Week</option>
            <option value="month">This Month</option>
          </select>
        </div>
      )}

      {operation === 'report.inventory.status' && (
        <p className="text-sm text-gray-500">
          No additional parameters required.
        </p>
      )}

      <button
        type="submit"
        disabled={isRunning}
        className="w-full rounded-md bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-brand-700 focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
      >
        {isRunning ? 'Running…' : 'Run Report'}
      </button>
    </form>
  );
}
