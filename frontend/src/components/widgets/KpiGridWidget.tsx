import type { WidgetLayout } from '../../types/module';

interface KpiItem {
  id: string;
  label: string;
  value: number | string;
  format?: string;
  comparison?: { deltaPercent?: number; direction?: string; isGood?: boolean; periodLabel?: string };
  sparkline?: number[] | null;
  icon?: string | null;
  variant?: 'default' | 'success' | 'warning' | 'danger' | 'info';
}

interface KpiGridData {
  columns?: number;
  items?: KpiItem[];
  rows?: KpiItem[]; // alternate key from provider
}

function formatValue(value: number | string, format?: string): string {
  if (typeof value === 'string') return value;
  if (!format) return value.toLocaleString('vi-VN');
  if (format.startsWith('currency:VND'))
    return new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value) + ' ₫';
  if (format.startsWith('currency:'))
    return new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  if (format.startsWith('percent:')) return `${value.toFixed(1)}%`;
  return value.toLocaleString('vi-VN');
}

const VARIANT_COLORS: Record<string, string> = {
  success: 'var(--success)',
  warning: 'var(--warning)',
  danger:  'var(--danger)',
  info:    'var(--info)',
  default: 'var(--brand)',
};

function Sparkline({ values }: { values: number[] }) {
  if (values.length < 2) return null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;
  const W = 60, H = 20;
  const pts = values.map((v, i) => {
    const x = (i / (values.length - 1)) * W;
    const y = H - ((v - min) / range) * H;
    return `${x},${y}`;
  }).join(' ');
  return (
    <svg viewBox={`0 0 ${W} ${H}`} width={W} height={H} style={{ opacity: 0.7 }}>
      <polyline points={pts} fill="none" stroke="var(--brand)" strokeWidth="1.5" strokeLinejoin="round" />
    </svg>
  );
}

export function KpiGridWidget({ data }: { data: unknown; widget?: WidgetLayout }) {
  const d = data as KpiGridData | null;
  if (!d) return <EmptyState />;

  const items: KpiItem[] = d.items ?? d.rows ?? [];
  const cols = d.columns ?? 4;

  if (!items.length) return <EmptyState />;

  return (
    <div
      className="grid gap-3 h-full"
      style={{ gridTemplateColumns: `repeat(${Math.min(cols, items.length)}, minmax(0, 1fr))` }}
    >
      {items.map(item => {
        const color = VARIANT_COLORS[item.variant ?? 'default'] ?? 'var(--brand)';
        const delta = item.comparison?.deltaPercent;
        const dir   = item.comparison?.direction;
        const isGood = item.comparison?.isGood ?? true;
        const positive = dir === 'up' ? true : dir === 'down' ? false : null;

        return (
          <div key={item.id} className="hdos-card p-4 flex flex-col gap-1 min-h-0">
            <p className="text-xs text-[--tx2] truncate">{item.label}</p>
            <p className="text-2xl font-bold tabular-nums leading-tight" style={{ color }}>
              {formatValue(item.value, item.format)}
            </p>
            {item.comparison && delta != null && (
              <span className={`text-xs font-medium ${
                (positive && isGood) || (!positive && !isGood)
                  ? 'text-[--success]'
                  : 'text-[--danger]'
              }`}>
                {positive ? '▲' : '▼'} {Math.abs(delta).toFixed(1)}%
                {item.comparison.periodLabel && (
                  <span className="text-[--tx3] font-normal ml-1">{item.comparison.periodLabel}</span>
                )}
              </span>
            )}
            {item.sparkline && item.sparkline.length > 1 && (
              <div className="mt-auto pt-1">
                <Sparkline values={item.sparkline} />
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

function EmptyState() {
  return (
    <div className="flex items-center justify-center h-full text-[--tx3] text-sm">
      Không có dữ liệu
    </div>
  );
}
