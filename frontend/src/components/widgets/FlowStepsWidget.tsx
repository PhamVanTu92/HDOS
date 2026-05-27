interface FlowStep {
  id: string;
  label: string;
  sublabel?: string | null;
  status: 'done' | 'current' | 'warning' | 'error' | 'pending';
  count?: number | null;
}

interface FlowStepsData {
  direction?: 'horizontal' | 'vertical';
  steps?: FlowStep[];
}

const STATUS_COLOR: Record<string, string> = {
  done:    'var(--success)',
  current: 'var(--brand)',
  warning: 'var(--warning)',
  error:   'var(--danger)',
  pending: 'var(--tx3)',
};

const STATUS_ICON: Record<string, string> = {
  done: '✓', current: '●', warning: '!', error: '✗', pending: '○',
};

export function FlowStepsWidget({ data }: { data: unknown }) {
  const d = data as FlowStepsData | null;
  if (!d?.steps?.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  const isHorizontal = (d.direction ?? 'horizontal') === 'horizontal';

  if (isHorizontal) {
    return (
      <div className="flex items-center gap-0 w-full overflow-x-auto py-2">
        {d.steps.map((step, i) => (
          <div key={step.id} className="flex items-center min-w-0">
            <div className="flex flex-col items-center gap-1 px-3">
              <div
                className="w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold shrink-0"
                style={{ background: `${STATUS_COLOR[step.status]}22`, color: STATUS_COLOR[step.status], border: `2px solid ${STATUS_COLOR[step.status]}` }}
              >
                {STATUS_ICON[step.status] ?? '?'}
              </div>
              <span className="text-xs text-[--tx] text-center leading-tight max-w-[72px] truncate">{step.label}</span>
              {step.sublabel && <span className="text-[10px] text-[--tx3]">{step.sublabel}</span>}
              {step.count != null && (
                <span className="badge badge-info text-[10px]">{step.count}</span>
              )}
            </div>
            {i < d.steps!.length - 1 && (
              <div className="h-0.5 w-6 shrink-0 bg-white/10" />
            )}
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-0 h-full overflow-y-auto">
      {d.steps.map((step, i) => (
        <div key={step.id} className="flex gap-3">
          <div className="flex flex-col items-center">
            <div
              className="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold shrink-0"
              style={{ background: `${STATUS_COLOR[step.status]}22`, color: STATUS_COLOR[step.status], border: `2px solid ${STATUS_COLOR[step.status]}` }}
            >
              {STATUS_ICON[step.status] ?? '?'}
            </div>
            {i < d.steps!.length - 1 && <div className="w-0.5 flex-1 mt-1 bg-white/10" />}
          </div>
          <div className="pb-3 min-w-0">
            <p className="text-sm text-[--tx]">{step.label}</p>
            {step.sublabel && <p className="text-xs text-[--tx2]">{step.sublabel}</p>}
            {step.count != null && <span className="badge badge-info text-[10px] mt-1">{step.count}</span>}
          </div>
        </div>
      ))}
    </div>
  );
}
