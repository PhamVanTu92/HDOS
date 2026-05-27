interface KpiComparison {
  deltaPercent?: number;
  direction?: string;
  isGood?: boolean;
  periodLabel?: string;
}

interface KpiData {
  value: number | string | null;
  format?: string;
  label?: string;
  comparison?: KpiComparison | null;
  sparkline?: number[] | null;
  // Legacy fields from old providers
  trend?: number | string;
  unit?: string;
}

function formatKpiValue(value: number | string | null, format?: string, unit?: string): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'string') return value;
  if (format?.startsWith('currency:VND'))
    return new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value) + (unit ? ' ' + unit : ' ₫');
  if (format?.startsWith('percent:')) return `${value.toFixed(1)}%`;
  return value.toLocaleString('vi-VN') + (unit ? ' ' + unit : '');
}

export function KpiWidget({ data }: { data: unknown }) {
  const d = data as KpiData | null;
  if (!d) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">—</div>;

  const val = d.value;
  const cmp = d.comparison;
  const delta = cmp?.deltaPercent;
  const dir   = cmp?.direction;
  const isGood = cmp?.isGood ?? true;
  const positive = dir === 'up';

  return (
    <div className="flex flex-col gap-2 p-2 h-full justify-center">
      {d.label && <p className="text-xs text-[--tx2]">{d.label}</p>}
      <p className="text-3xl font-black tabular-nums" style={{ color: 'var(--brand)' }}>
        {formatKpiValue(val, d.format, d.unit)}
      </p>
      {cmp && delta != null && (
        <span className={`text-sm font-medium flex items-center gap-1 ${
          (positive && isGood) || (!positive && !isGood) ? 'text-[--success]' : 'text-[--danger]'
        }`}>
          {positive ? '▲' : '▼'} {Math.abs(delta).toFixed(1)}%
          {cmp.periodLabel && <span className="text-[--tx3] font-normal text-xs">{cmp.periodLabel}</span>}
        </span>
      )}
    </div>
  );
}
