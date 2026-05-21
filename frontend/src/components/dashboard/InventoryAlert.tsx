interface InventoryAlertProps {
  alerts: string[];
  loading?: boolean;
}

export function InventoryAlert({ alerts, loading = false }: InventoryAlertProps) {
  return (
    <div className="rounded-xl bg-white shadow-sm">
      <div className="flex items-center gap-2 border-b border-gray-100 px-5 py-3">
        <svg
          className="h-4 w-4 text-amber-500"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"
          />
        </svg>
        <h3 className="text-sm font-semibold text-gray-700">Inventory Alerts</h3>
      </div>

      <ul className="divide-y divide-gray-50 px-5">
        {loading ? (
          Array.from({ length: 3 }).map((_, i) => (
            <li key={i} className="flex items-center gap-3 py-3">
              <div className="h-2 w-2 animate-pulse rounded-full bg-gray-200" />
              <div className="h-4 flex-1 animate-pulse rounded bg-gray-100" />
            </li>
          ))
        ) : alerts.length === 0 ? (
          <li className="py-6 text-center text-sm text-gray-400">
            No active alerts
          </li>
        ) : (
          alerts.map((alert, i) => (
            <li key={i} className="flex items-start gap-3 py-3">
              <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-amber-400" />
              <p className="text-sm text-gray-700">{alert}</p>
            </li>
          ))
        )}
      </ul>
    </div>
  );
}
