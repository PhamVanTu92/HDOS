import { useEffect } from 'react';
import { useDashboard } from '../hooks/useDashboard';
import { KpiCard } from '../components/dashboard/KpiCard';
import { SalesChart } from '../components/dashboard/SalesChart';
import { RegionTable } from '../components/dashboard/RegionTable';
import { InventoryAlert } from '../components/dashboard/InventoryAlert';

export function Dashboard() {
  const { summary, isLoading, error, submit } = useDashboard();

  // Auto-load on mount
  useEffect(() => {
    submit();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-800">Dashboard</h1>
          <p className="text-sm text-gray-500">
            Real-time KPIs — auto-refreshes on WidgetStale events
          </p>
        </div>
        <button
          onClick={() => submit()}
          disabled={isLoading}
          className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          {isLoading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {/* Error banner */}
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* KPI Cards */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard
          label="Total Revenue"
          value={
            summary
              ? summary.totalRevenue.toLocaleString(undefined, {
                  style: 'currency',
                  currency: 'USD',
                  maximumFractionDigits: 0,
                })
              : '—'
          }
          accent="blue"
          loading={isLoading}
        />
        <KpiCard
          label="Total Units"
          value={summary ? summary.totalUnits.toLocaleString() : '—'}
          accent="green"
          loading={isLoading}
        />
        <KpiCard
          label="Top Region"
          value={summary?.topRegion ?? '—'}
          accent="purple"
          loading={isLoading}
        />
        <KpiCard
          label="Top Product"
          value={summary?.topProduct ?? '—'}
          accent="orange"
          loading={isLoading}
        />
      </div>

      {/* Charts row */}
      <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
        {/* Sales chart spans 2/3 */}
        <div className="xl:col-span-2">
          <SalesChart data={null} loading={isLoading} />
        </div>

        {/* Inventory alerts */}
        <InventoryAlert
          alerts={summary?.alerts ?? []}
          loading={isLoading}
        />
      </div>

      {/* Region table — placeholder until regional data hook is wired */}
      <RegionTable rows={[]} loading={isLoading} />
    </div>
  );
}
