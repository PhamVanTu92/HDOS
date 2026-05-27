interface ProgressRow {
  id: string;
  label: string;
  sublabel?: string | null;
  current: number;
  max: number;
  percent?: number;
  colorThresholds?: { from: number; to: number; color: string }[];
  badge?: string | null;
  badgeVariant?: string | null;
}

interface ProgressRowsData {
  rows?: ProgressRow[];
  showPercent?: boolean;
  showValues?: boolean;
}

function getColor(percent: number, thresholds?: { from: number; to: number; color: string }[]): string {
  if (!thresholds?.length) return 'var(--brand)';
  const t = thresholds.find(t => percent >= t.from && percent < t.to);
  const name = t?.color ?? 'info';
  const map: Record<string, string> = {
    success: 'var(--success)', warning: 'var(--warning)',
    danger: 'var(--danger)', info: 'var(--info)',
  };
  return map[name] ?? 'var(--brand)';
}

function getBadgeClass(variant?: string | null): string {
  const map: Record<string, string> = {
    success: 'badge-success', warning: 'badge-warning',
    danger: 'badge-danger', info: 'badge badge-info',
  };
  return variant ? (map[variant] ?? 'badge badge-neutral') : 'badge badge-neutral';
}

export function ProgressRowsWidget({ data }: { data: unknown }) {
  const d = data as ProgressRowsData | null;
  if (!d?.rows?.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  return (
    <div className="flex flex-col gap-2.5 h-full overflow-y-auto">
      {d.rows.map(row => {
        const pct = row.percent ?? (row.max > 0 ? (row.current / row.max) * 100 : 0);
        const color = getColor(pct, row.colorThresholds);
        return (
          <div key={row.id} className="flex flex-col gap-1">
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <span className="text-sm text-[--tx] truncate block">{row.label}</span>
                {row.sublabel && <span className="text-xs text-[--tx2]">{row.sublabel}</span>}
              </div>
              <div className="flex items-center gap-2 shrink-0">
                {row.badge && (
                  <span className={`badge ${getBadgeClass(row.badgeVariant)}`}>{row.badge}</span>
                )}
                {d.showPercent !== false && (
                  <span className="text-xs font-semibold tabular-nums text-[--tx2]">{pct.toFixed(0)}%</span>
                )}
              </div>
            </div>
            <div className="h-2 rounded-full bg-white/10 overflow-hidden">
              <div
                className="h-full rounded-full transition-all duration-500"
                style={{ width: `${Math.min(pct, 100)}%`, background: color }}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}
