import { useEffect, useCallback } from 'react';
import { useDashboard } from '../hooks/useDashboard';
import { useSalesTrend } from '../hooks/useSalesTrend';
import { useRegionalPerformance } from '../hooks/useRegionalPerformance';
import { KpiCard } from '../components/dashboard/KpiCard';
import { SalesChart } from '../components/dashboard/SalesChart';
import { RegionTable } from '../components/dashboard/RegionTable';
import { InventoryAlert } from '../components/dashboard/InventoryAlert';

export function Dashboard() {
  const { summary, isLoading,       error,       submit        } = useDashboard();
  const { trend,   isLoading: trendLoading,      submit: submitTrend    } = useSalesTrend();
  const { regions, isLoading: regionLoading,     submit: submitRegions  } = useRegionalPerformance();

  // Auto-load all widgets on mount.
  useEffect(() => {
    submit();
    submitTrend();
    submitRegions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Manual refresh button reloads every widget simultaneously.
  const refreshAll = useCallback(() => {
    submit();
    submitTrend();
    submitRegions();
  }, [submit, submitTrend, submitRegions]);

  const anyLoading = isLoading || trendLoading || regionLoading;

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
          onClick={refreshAll}
          disabled={anyLoading}
          className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          {anyLoading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {/* Error banner — show first non-null error */}
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
        {/* Sales trend chart — spans 2/3, live data from report.sales.trend */}
        <div className="xl:col-span-2">
          <SalesChart data={trend} loading={trendLoading} />
        </div>

        {/* Inventory alerts — from dashboard summary */}
        <InventoryAlert
          alerts={summary?.alerts ?? []}
          loading={isLoading}
        />
      </div>

      {/* Regional performance table — live data from report.regional.performance */}
      <RegionTable rows={regions} loading={regionLoading} />
    </div>
  );
}
