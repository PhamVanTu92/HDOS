interface FlowStage {
  id: string;
  label: string;
  count: number;
  avgWaitMin?: number | null;
  status?: 'ok' | 'warning' | 'danger' | null;
}

interface PatientFlowData {
  stages?: FlowStage[];
  totalPatients?: number;
}

const STATUS_COLOR: Record<string, string> = {
  ok: 'var(--success)', warning: 'var(--warning)', danger: 'var(--danger)',
};

export function PatientFlowWidget({ data }: { data: unknown }) {
  const d = data as PatientFlowData | null;
  if (!d?.stages?.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  const maxCount = Math.max(...d.stages.map(s => s.count), 1);

  return (
    <div className="flex flex-col h-full">
      {d.totalPatients != null && (
        <p className="text-xs text-[--tx2] mb-3">
          Tổng: <span className="font-bold text-[--tx]">{d.totalPatients}</span> bệnh nhân
        </p>
      )}
      <div className="flex items-end gap-2 flex-1 min-h-0">
        {d.stages.map((stage) => {
          const pct = (stage.count / maxCount) * 100;
          const color = STATUS_COLOR[stage.status ?? 'ok'] ?? 'var(--brand)';
          return (
            <div key={stage.id} className="flex flex-col items-center gap-1 flex-1 min-w-0 h-full">
              <span className="text-xs font-bold tabular-nums" style={{ color }}>{stage.count}</span>
              {stage.avgWaitMin != null && (
                <span className="text-[10px] text-[--tx3]">{stage.avgWaitMin}′</span>
              )}
              <div className="w-full flex-1 flex items-end">
                <div
                  className="w-full rounded-t-md transition-all duration-700"
                  style={{ height: `${pct}%`, background: `${color}55`, border: `1px solid ${color}`, minHeight: 4 }}
                />
              </div>
              <p className="text-[10px] text-[--tx2] text-center leading-tight truncate w-full">{stage.label}</p>
            </div>
          );
        })}
      </div>
    </div>
  );
}
