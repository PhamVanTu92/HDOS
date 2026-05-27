interface RiskTier {
  level: 1 | 2 | 3 | 4;
  label: string;
  count: number;
  percent: number;
  color: string;
  action?: string | null;
  changeFromPrev?: number | null;
}

interface RiskTiersData {
  tiers?: RiskTier[];
  total?: number;
}

const TIER_COLORS: Record<string, string> = {
  danger: 'var(--danger)', warning: 'var(--warning)',
  info: 'var(--info)', success: 'var(--success)',
};

export function RiskTiersWidget({ data }: { data: unknown }) {
  const d = data as RiskTiersData | null;
  if (!d?.tiers?.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  return (
    <div className="flex flex-col gap-2.5 h-full overflow-y-auto">
      {d.total != null && (
        <p className="text-xs text-[--tx2]">Tổng: <span className="font-bold text-[--tx]">{d.total.toLocaleString('vi-VN')}</span></p>
      )}
      {d.tiers.map(tier => {
        const color = TIER_COLORS[tier.color] ?? 'var(--info)';
        return (
          <div key={tier.level} className="flex items-center gap-3">
            <div
              className="w-8 h-8 rounded-lg flex items-center justify-center text-sm font-bold shrink-0"
              style={{ background: `${color}22`, color }}
            >
              T{tier.level}
            </div>
            <div className="flex-1 min-w-0">
              <div className="flex items-center justify-between gap-2 mb-1">
                <span className="text-xs text-[--tx] truncate">{tier.label}</span>
                <div className="flex items-center gap-1 shrink-0">
                  <span className="text-sm font-bold tabular-nums text-[--tx]">{tier.count.toLocaleString('vi-VN')}</span>
                  {tier.changeFromPrev != null && (
                    <span className={`text-[10px] ${tier.changeFromPrev > 0 ? 'text-[--danger]' : 'text-[--success]'}`}>
                      {tier.changeFromPrev > 0 ? '+' : ''}{tier.changeFromPrev}
                    </span>
                  )}
                </div>
              </div>
              <div className="h-1.5 rounded-full bg-white/10 overflow-hidden">
                <div className="h-full rounded-full" style={{ width: `${tier.percent}%`, background: color }} />
              </div>
              {tier.action && <p className="text-[10px] text-[--tx3] mt-0.5">{tier.action}</p>}
            </div>
          </div>
        );
      })}
    </div>
  );
}
