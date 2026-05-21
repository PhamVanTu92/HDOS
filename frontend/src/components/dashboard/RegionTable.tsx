import type { RegionPerformanceRow } from '../../types/contracts';

interface RegionTableProps {
  rows: RegionPerformanceRow[];
  loading?: boolean;
}

function AchievementBadge({ pct }: { pct: number }) {
  const cls =
    pct >= 100
      ? 'bg-green-100 text-green-700'
      : pct >= 75
      ? 'bg-yellow-100 text-yellow-700'
      : 'bg-red-100 text-red-700';
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${cls}`}>
      {pct.toFixed(1)}%
    </span>
  );
}

export function RegionTable({ rows, loading = false }: RegionTableProps) {
  return (
    <div className="rounded-xl bg-white shadow-sm">
      <div className="border-b border-gray-100 px-5 py-3">
        <h3 className="text-sm font-semibold text-gray-700">
          Regional Performance
        </h3>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-gray-50 text-left text-xs font-semibold uppercase text-gray-500">
              <th className="px-5 py-3">Region</th>
              <th className="px-5 py-3 text-right">Revenue</th>
              <th className="px-5 py-3 text-right">Units</th>
              <th className="px-5 py-3 text-right">Target</th>
              <th className="px-5 py-3 text-right">Achievement</th>
            </tr>
          </thead>
          <tbody>
            {loading
              ? Array.from({ length: 4 }).map((_, i) => (
                  <tr key={i} className="border-t border-gray-50">
                    {Array.from({ length: 5 }).map((_, j) => (
                      <td key={j} className="px-5 py-3">
                        <div className="h-4 w-full animate-pulse rounded bg-gray-100" />
                      </td>
                    ))}
                  </tr>
                ))
              : rows.map((row) => (
                  <tr
                    key={row.name}
                    className="border-t border-gray-50 hover:bg-gray-50"
                  >
                    <td className="px-5 py-3 font-medium text-gray-800">
                      {row.name}
                    </td>
                    <td className="px-5 py-3 text-right text-gray-600">
                      {row.revenue.toLocaleString()}
                    </td>
                    <td className="px-5 py-3 text-right text-gray-600">
                      {row.units.toLocaleString()}
                    </td>
                    <td className="px-5 py-3 text-right text-gray-600">
                      {row.target.toLocaleString()}
                    </td>
                    <td className="px-5 py-3 text-right">
                      <AchievementBadge pct={row.achievementPct} />
                    </td>
                  </tr>
                ))}
          </tbody>
        </table>
        {!loading && rows.length === 0 && (
          <p className="py-8 text-center text-sm text-gray-400">
            No regional data available
          </p>
        )}
      </div>
    </div>
  );
}
