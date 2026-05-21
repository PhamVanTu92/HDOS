interface KpiCardProps {
  label: string;
  value: string | number;
  subtitle?: string;
  accent?: 'blue' | 'green' | 'purple' | 'orange';
  loading?: boolean;
}

const ACCENT_STYLES = {
  blue: 'border-l-brand-500 bg-brand-50',
  green: 'border-l-green-500 bg-green-50',
  purple: 'border-l-purple-500 bg-purple-50',
  orange: 'border-l-orange-500 bg-orange-50',
} as const;

const ACCENT_TEXT = {
  blue: 'text-brand-700',
  green: 'text-green-700',
  purple: 'text-purple-700',
  orange: 'text-orange-700',
} as const;

export function KpiCard({
  label,
  value,
  subtitle,
  accent = 'blue',
  loading = false,
}: KpiCardProps) {
  return (
    <div
      className={`rounded-xl border-l-4 bg-white p-5 shadow-sm ${ACCENT_STYLES[accent]}`}
    >
      <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-gray-500">
        {label}
      </p>
      {loading ? (
        <div className="mt-2 h-7 w-32 animate-pulse rounded bg-gray-200" />
      ) : (
        <p className={`text-2xl font-bold ${ACCENT_TEXT[accent]}`}>{value}</p>
      )}
      {subtitle && !loading && (
        <p className="mt-1 text-xs text-gray-500 truncate">{subtitle}</p>
      )}
    </div>
  );
}
