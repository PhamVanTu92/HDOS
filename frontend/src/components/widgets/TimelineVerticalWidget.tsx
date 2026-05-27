interface TimelineEvent {
  id?:     string;
  time:    string;
  label:   string;
  detail?: string | null;
  type?:   'admission' | 'discharge' | 'medication' | 'procedure' | 'observation' | 'alert' | string;
  status?: 'ok' | 'warning' | 'error' | 'info' | string;
  actor?:  string | null;
}

interface TimelineData {
  rows:         TimelineEvent[];
  patientId?:   string;
  patientName?: string;
}

const TYPE_COLOR: Record<string, string> = {
  admission:   'var(--brand)',
  discharge:   'var(--success)',
  medication:  'var(--info)',
  procedure:   'var(--warning)',
  observation: 'var(--tx3)',
  alert:       'var(--danger)',
};

const STATUS_OVERRIDE: Record<string, string> = {
  error:   'var(--danger)',
  warning: 'var(--warning)',
};

function formatTime(raw: string): string {
  if (raw.includes('T')) {
    // ISO datetime — extract HH:mm
    const timePart = raw.split('T')[1] ?? '';
    return timePart.slice(0, 5);
  }
  return raw.slice(0, 5);
}

function dotColor(event: TimelineEvent): string {
  if (event.status && STATUS_OVERRIDE[event.status]) {
    return STATUS_OVERRIDE[event.status];
  }
  return TYPE_COLOR[event.type ?? ''] ?? 'var(--tx3)';
}

interface StatusBadgeProps {
  status: string;
}

function StatusBadge({ status }: StatusBadgeProps) {
  const label =
    status === 'error'   ? 'Lỗi'      :
    status === 'warning' ? 'Cảnh báo' :
    status === 'info'    ? 'Thông tin' :
    status;

  const cls =
    status === 'error'   ? 'badge badge-danger'  :
    status === 'warning' ? 'badge badge-warning' :
    status === 'info'    ? 'badge badge-info'    :
    'badge';

  return (
    <span className={`${cls} text-[10px]`}>{label}</span>
  );
}

export function TimelineVerticalWidget({ data }: { data: unknown }) {
  const d = data as TimelineData | null;
  const rows: TimelineEvent[] = Array.isArray(d?.rows) ? d!.rows : [];

  if (!rows.length) {
    return (
      <div className="flex items-center justify-center h-full text-[--tx3] text-sm">
        Không có sự kiện nào
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full overflow-y-auto px-1">
      {d?.patientName && (
        <p className="text-xs text-[--tx2] mb-3">
          Bệnh nhân: <span className="font-medium text-[--tx]">{d.patientName}</span>
        </p>
      )}

      <div className="relative pl-6">
        {/* Vertical connector line */}
        <div
          className="absolute top-2 bottom-2 w-0.5"
          style={{ left: '8px', background: 'var(--border-md)' }}
        />

        {rows.map((event, idx) => {
          const color = dotColor(event);
          const key = event.id ?? `${event.time}-${idx}`;
          const showStatus = event.status && event.status !== 'ok' && event.status !== 'info';

          return (
            <div key={key} className="relative mb-4 last:mb-0">
              {/* Dot */}
              <div
                className="absolute w-3 h-3 rounded-full border-2"
                style={{
                  left: '-18px',
                  top: '4px',
                  borderColor: color,
                  background: 'var(--card)',
                }}
              />

              {/* Content */}
              <div className="pl-2">
                <div className="flex items-baseline gap-2 flex-wrap">
                  <span className="text-[10px] text-[--tx3] font-mono w-10 shrink-0 tabular-nums">
                    {formatTime(event.time)}
                  </span>
                  <span className="text-xs font-medium text-[--tx]">{event.label}</span>
                  {showStatus && <StatusBadge status={event.status!} />}
                </div>
                {event.detail && (
                  <p className="text-[10px] text-[--tx2] mt-0.5 pl-12">{event.detail}</p>
                )}
                {event.actor && (
                  <p className="text-[10px] text-[--tx3] mt-0.5 pl-12">— {event.actor}</p>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
