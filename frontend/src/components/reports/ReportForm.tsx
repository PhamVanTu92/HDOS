import { useState, type FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getProducts } from '../../api/dataManagement';
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

  // Channel comparison params
  const [channelFromDate, setChannelFromDate] = useState('');
  const [channelToDate, setChannelToDate] = useState('');

  // Product detail params
  const [productName, setProductName] = useState('');
  const [productFromDate, setProductFromDate] = useState('');
  const [productToDate, setProductToDate] = useState('');

  // Top performers params
  const [topPeriod, setTopPeriod] = useState<'week' | 'month' | 'quarter'>('month');

  const { data: products = [] } = useQuery({
    queryKey: ['products'],
    queryFn: getProducts,
    enabled:
      operation === 'report.product.detail',
  });

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
      case 'report.channel.comparison':
        params = { fromDate: channelFromDate, toDate: channelToDate };
        break;
      case 'report.product.detail':
        params = { productName, fromDate: productFromDate, toDate: productToDate };
        break;
      case 'report.top.performers':
        params = { period: topPeriod };
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
          Không cần tham số bổ sung.
        </p>
      )}

      {operation === 'report.channel.comparison' && (
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">
              Từ ngày
            </label>
            <input
              type="date"
              value={channelFromDate}
              onChange={(e) => setChannelFromDate(e.target.value)}
              required
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">
              Đến ngày
            </label>
            <input
              type="date"
              value={channelToDate}
              onChange={(e) => setChannelToDate(e.target.value)}
              required
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            />
          </div>
        </div>
      )}

      {operation === 'report.product.detail' && (
        <div className="space-y-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">
              Sản phẩm
            </label>
            <select
              value={productName}
              onChange={(e) => setProductName(e.target.value)}
              required
              className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            >
              <option value="">-- Chọn sản phẩm --</option>
              {products.slice(0, 10).map((p) => (
                <option key={p.productId} value={p.name}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">
                Từ ngày
              </label>
              <input
                type="date"
                value={productFromDate}
                onChange={(e) => setProductFromDate(e.target.value)}
                required
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">
                Đến ngày
              </label>
              <input
                type="date"
                value={productToDate}
                onChange={(e) => setProductToDate(e.target.value)}
                required
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>
          </div>
        </div>
      )}

      {operation === 'report.top.performers' && (
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            Kỳ
          </label>
          <select
            value={topPeriod}
            onChange={(e) =>
              setTopPeriod(e.target.value as 'week' | 'month' | 'quarter')
            }
            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          >
            <option value="week">Tuần này</option>
            <option value="month">Tháng này</option>
            <option value="quarter">Quý này</option>
          </select>
        </div>
      )}

      <button
        type="submit"
        disabled={isRunning}
        className="w-full rounded-md bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-brand-700 focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
      >
        {isRunning ? 'Đang chạy…' : 'Chạy báo cáo'}
      </button>
    </form>
  );
}
