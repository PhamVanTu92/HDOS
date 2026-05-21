import type { RequestStatus } from '../../types/contracts';

interface ProgressBarProps {
  pct: number;
  status: RequestStatus | null;
}

const STATUS_LABEL: Record<string, string> = {
  Queued: 'Queued…',
  Processing: 'Processing…',
  Completed: 'Completed',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
};

const STATUS_COLOR: Record<string, string> = {
  Queued: 'bg-brand-400',
  Processing: 'bg-brand-600',
  Completed: 'bg-green-500',
  Failed: 'bg-red-500',
  Cancelled: 'bg-gray-400',
};

export function ProgressBar({ pct, status }: ProgressBarProps) {
  if (!status) return null;
  const label = STATUS_LABEL[status] ?? status;
  const colorCls = STATUS_COLOR[status] ?? 'bg-brand-600';

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-xs text-gray-600">
        <span>{label}</span>
        <span>{pct}%</span>
      </div>
      <div className="h-2 w-full overflow-hidden rounded-full bg-gray-100">
        <div
          className={`h-full rounded-full transition-all duration-500 ${colorCls}`}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
