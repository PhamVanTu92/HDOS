interface Alert {
  id: string;
  level: 1 | 2 | 3;
  title: string;
  subtitle?: string | null;
  timeLabel?: string;
  acknowledged?: boolean;
  runbookId?: string | null;
}

interface AlertListData {
  alerts?: Alert[];
  totalUnacknowledged?: number;
}

const LEVEL_CLASSES: Record<number, string> = {
  1: 'badge-L1',
  2: 'badge-L2',
  3: 'badge-L3',
};

const LEVEL_LABELS: Record<number, string> = { 1: 'L1 Cấp cứu', 2: 'L2 Khẩn', 3: 'L3 Lưu ý' };
const LEVEL_BORDER: Record<number, string> = {
  1: 'border-l-[var(--danger)]',
  2: 'border-l-[var(--warning)]',
  3: 'border-l-[var(--info)]',
};

export function AlertListWidget({ data }: { data: unknown }) {
  const d = data as AlertListData | null;
  if (!d?.alerts?.length) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-1">
        <span className="text-2xl">✓</span>
        <p className="text-sm text-[--success]">Không có cảnh báo</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2 h-full overflow-y-auto">
      {d.alerts.map(alert => (
        <div
          key={alert.id}
          className={`flex gap-3 p-3 rounded-lg border-l-2 ${LEVEL_BORDER[alert.level] ?? 'border-l-[--info]'} bg-white/5 ${alert.acknowledged ? 'opacity-50' : ''}`}
        >
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-0.5">
              <span className={`badge ${LEVEL_CLASSES[alert.level] ?? 'badge-neutral'} text-[10px]`}>
                {LEVEL_LABELS[alert.level] ?? `L${alert.level}`}
              </span>
              {alert.timeLabel && (
                <span className="text-[10px] text-[--tx3]">{alert.timeLabel}</span>
              )}
              {alert.acknowledged && (
                <span className="text-[10px] text-[--tx3] ml-auto">✓ Đã xử lý</span>
              )}
            </div>
            <p className="text-sm font-medium text-[--tx] leading-tight">{alert.title}</p>
            {alert.subtitle && (
              <p className="text-xs text-[--tx2] mt-0.5 truncate">{alert.subtitle}</p>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
