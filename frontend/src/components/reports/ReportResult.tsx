import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
  LineChart,
  Line,
} from 'recharts';
import type {
  DashboardSummary,
  SalesTrend,
  InventoryStatus,
  RegionalPerformance,
  ReportOperation,
  StockStatus,
} from '../../types/contracts';

interface ReportResultProps {
  operation: ReportOperation;
  data: unknown;
  requestId: string;
}

function DownloadButton({
  data,
  filename,
}: {
  data: unknown;
  filename: string;
}) {
  const download = () => {
    const blob = new Blob([JSON.stringify(data, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <button
      onClick={download}
      className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
    >
      Download JSON
    </button>
  );
}

const STOCK_BADGE: Record<StockStatus, string> = {
  ok: 'bg-green-100 text-green-700',
  low: 'bg-yellow-100 text-yellow-700',
  out: 'bg-red-100 text-red-700',
};

function DashboardSummaryResult({
  data,
  requestId,
}: {
  data: DashboardSummary;
  requestId: string;
}) {
  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <DownloadButton data={data} filename={`dashboard-summary-${requestId}.json`} />
      </div>
      <div className="grid grid-cols-2 gap-4">
        {[
          { label: 'Total Revenue', value: data.totalRevenue.toLocaleString() },
          { label: 'Total Units', value: data.totalUnits.toLocaleString() },
          { label: 'Top Region', value: data.topRegion },
          { label: 'Top Product', value: data.topProduct },
        ].map((kv) => (
          <div key={kv.label} className="rounded-lg border border-gray-100 bg-gray-50 p-4">
            <p className="text-xs text-gray-500">{kv.label}</p>
            <p className="mt-1 text-lg font-bold text-gray-800">{kv.value}</p>
          </div>
        ))}
      </div>
      {data.alerts.length > 0 && (
        <div>
          <p className="mb-2 text-xs font-semibold uppercase text-gray-500">
            Alerts
          </p>
          <ul className="space-y-1">
            {data.alerts.map((a, i) => (
              <li key={i} className="flex items-start gap-2 text-sm text-gray-700">
                <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-amber-400" />
                {a}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function SalesTrendResult({
  data,
  requestId,
}: {
  data: SalesTrend;
  requestId: string;
}) {
  const chartData = data.labels.map((label, i) => {
    const row: Record<string, string | number> = { label };
    data.series.forEach((s) => {
      row[s.name] = s.data[i] ?? 0;
    });
    return row;
  });

  const COLORS = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444'];

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <DownloadButton data={data} filename={`sales-trend-${requestId}.json`} />
      </div>
      <ResponsiveContainer width="100%" height={300}>
        <LineChart data={chartData}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis dataKey="label" tick={{ fontSize: 11 }} tickLine={false} />
          <YAxis tick={{ fontSize: 11 }} tickLine={false} axisLine={false} />
          <Tooltip />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          {data.series.map((s, i) => (
            <Line
              key={s.name}
              type="monotone"
              dataKey={s.name}
              stroke={COLORS[i % COLORS.length]}
              strokeWidth={2}
              dot={false}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function InventoryStatusResult({
  data,
  requestId,
}: {
  data: InventoryStatus;
  requestId: string;
}) {
  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <DownloadButton data={data} filename={`inventory-${requestId}.json`} />
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-gray-50 text-left text-xs font-semibold uppercase text-gray-500">
              <th className="px-4 py-3">Product</th>
              <th className="px-4 py-3">Category</th>
              <th className="px-4 py-3 text-right">Stock</th>
              <th className="px-4 py-3">Status</th>
            </tr>
          </thead>
          <tbody>
            {data.products.map((p) => (
              <tr key={p.name} className="border-t border-gray-50">
                <td className="px-4 py-3 font-medium text-gray-800">{p.name}</td>
                <td className="px-4 py-3 text-gray-600">{p.category}</td>
                <td className="px-4 py-3 text-right text-gray-600">
                  {p.stock.toLocaleString()}
                </td>
                <td className="px-4 py-3">
                  <span
                    className={`rounded-full px-2 py-0.5 text-xs font-medium ${STOCK_BADGE[p.status]}`}
                  >
                    {p.status.toUpperCase()}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function RegionalPerformanceResult({
  data,
  requestId,
}: {
  data: RegionalPerformance;
  requestId: string;
}) {
  const chartData = data.regions.map((r) => ({
    name: r.name,
    Revenue: r.revenue,
    Target: r.target,
  }));

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <DownloadButton data={data} filename={`regional-${requestId}.json`} />
      </div>
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={chartData}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis dataKey="name" tick={{ fontSize: 11 }} tickLine={false} />
          <YAxis tick={{ fontSize: 11 }} tickLine={false} axisLine={false} />
          <Tooltip />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          <Bar dataKey="Revenue" fill="#3b82f6" radius={[3, 3, 0, 0]} />
          <Bar dataKey="Target" fill="#d1d5db" radius={[3, 3, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-gray-50 text-left text-xs font-semibold uppercase text-gray-500">
              <th className="px-4 py-3">Region</th>
              <th className="px-4 py-3 text-right">Revenue</th>
              <th className="px-4 py-3 text-right">Units</th>
              <th className="px-4 py-3 text-right">Target</th>
              <th className="px-4 py-3 text-right">Achievement</th>
            </tr>
          </thead>
          <tbody>
            {data.regions.map((r) => (
              <tr key={r.name} className="border-t border-gray-50">
                <td className="px-4 py-3 font-medium text-gray-800">{r.name}</td>
                <td className="px-4 py-3 text-right text-gray-600">
                  {r.revenue.toLocaleString()}
                </td>
                <td className="px-4 py-3 text-right text-gray-600">
                  {r.units.toLocaleString()}
                </td>
                <td className="px-4 py-3 text-right text-gray-600">
                  {r.target.toLocaleString()}
                </td>
                <td className="px-4 py-3 text-right">
                  <span
                    className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                      r.achievementPct >= 100
                        ? 'bg-green-100 text-green-700'
                        : r.achievementPct >= 75
                        ? 'bg-yellow-100 text-yellow-700'
                        : 'bg-red-100 text-red-700'
                    }`}
                  >
                    {r.achievementPct.toFixed(1)}%
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function ReportResult({ operation, data, requestId }: ReportResultProps) {
  switch (operation) {
    case 'report.dashboard.summary':
      return (
        <DashboardSummaryResult
          data={data as DashboardSummary}
          requestId={requestId}
        />
      );
    case 'report.sales.trend':
      return (
        <SalesTrendResult data={data as SalesTrend} requestId={requestId} />
      );
    case 'report.inventory.status':
      return (
        <InventoryStatusResult
          data={data as InventoryStatus}
          requestId={requestId}
        />
      );
    case 'report.regional.performance':
      return (
        <RegionalPerformanceResult
          data={data as RegionalPerformance}
          requestId={requestId}
        />
      );
    default:
      return (
        <pre className="overflow-auto rounded-md bg-gray-50 p-4 text-xs text-gray-700">
          {JSON.stringify(data, null, 2)}
        </pre>
      );
  }
}
