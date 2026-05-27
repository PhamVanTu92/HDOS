interface BedItem {
  id:        string;
  label:     string;
  ward?:     string;
  status:    'available' | 'occupied' | 'reserved' | 'cleaning' | string;
  patient?:  string | null;
  admitDate?: string | null;
  daysStay?:  number | null;
}

interface BedGridData {
  rows:       BedItem[];
  total?:     number;
  available?: number;
  occupied?:  number;
  reserved?:  number;
  cleaning?:  number;
}

const STATUS_LABEL: Record<string, string> = {
  available: 'Trống',
  occupied:  'Đang dùng',
  reserved:  'Giữ chỗ',
  cleaning:  'Đang dọn',
};

const STATUS_BORDER: Record<string, string> = {
  available: '2px solid var(--success)',
  occupied:  '2px solid var(--danger)',
  reserved:  '2px solid var(--warning)',
  cleaning:  '2px solid var(--info)',
};

const STATUS_BG: Record<string, string> = {
  available: 'var(--success-bg)',
  occupied:  'var(--danger-bg)',
  reserved:  'var(--warning-bg)',
  cleaning:  'var(--info-bg)',
};

const STATUS_TEXT_COLOR: Record<string, string> = {
  available: 'var(--success)',
  occupied:  'var(--danger)',
  reserved:  'var(--warning)',
  cleaning:  'var(--info)',
};

function deriveCount(rows: BedItem[], status: string): number {
  return rows.filter(r => r.status === status).length;
}

function StatChip({
  icon,
  label,
  count,
  color,
}: {
  icon: string;
  label: string;
  count: number;
  color: string;
}) {
  return (
    <div
      className="flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium"
      style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}
    >
      <span>{icon}</span>
      <span style={{ color: 'var(--tx2)' }}>{label}:</span>
      <span style={{ color, fontVariantNumeric: 'tabular-nums' }}>{count}</span>
    </div>
  );
}

function BedCell({ bed }: { bed: BedItem }) {
  const border     = STATUS_BORDER[bed.status] ?? '2px solid var(--border)';
  const background = STATUS_BG[bed.status]     ?? 'var(--card)';
  const textColor  = STATUS_TEXT_COLOR[bed.status] ?? 'var(--tx3)';
  const isOccupied = bed.status === 'occupied';

  return (
    <div
      className="rounded-md p-2 flex flex-col gap-0.5 overflow-hidden"
      style={{ border, background, minHeight: '72px' }}
    >
      {/* Bed label */}
      <p
        className="text-xs font-bold leading-tight truncate"
        style={{ color: 'var(--tx)' }}
      >
        {bed.label}
      </p>

      {isOccupied && bed.patient ? (
        <>
          <p
            className="text-[10px] leading-tight truncate"
            style={{ color: 'var(--tx2)' }}
          >
            {bed.patient}
          </p>
          {bed.daysStay != null && (
            <p
              className="text-[10px] leading-tight"
              style={{ color: 'var(--tx3)' }}
            >
              Ngày {bed.daysStay}
            </p>
          )}
        </>
      ) : (
        <p
          className="text-[10px] font-medium leading-tight mt-auto"
          style={{ color: textColor }}
        >
          {STATUS_LABEL[bed.status] ?? bed.status}
        </p>
      )}
    </div>
  );
}

export function BedGridWidget({ data }: { data: unknown }) {
  const d = data as BedGridData | null;
  if (!d?.rows?.length) {
    return (
      <div className="flex items-center justify-center h-full text-sm" style={{ color: 'var(--tx3)' }}>
        Không có dữ liệu giường bệnh
      </div>
    );
  }

  const rows      = d.rows;
  const total     = d.total     ?? rows.length;
  const available = d.available ?? deriveCount(rows, 'available');
  const occupied  = d.occupied  ?? deriveCount(rows, 'occupied');
  const cleaning  = (d.cleaning ?? deriveCount(rows, 'cleaning')) +
                    (d.reserved  ?? deriveCount(rows, 'reserved'));

  return (
    <div className="flex flex-col h-full gap-3 min-h-0">
      {/* Summary stats bar */}
      <div className="flex flex-wrap gap-2 shrink-0">
        <StatChip icon="🛏"  label="Tổng"      count={total}     color="var(--tx)" />
        <StatChip icon="✅"  label="Trống"      count={available} color="var(--success)" />
        <StatChip icon="🔴"  label="Đang dùng"  count={occupied}  color="var(--danger)" />
        <StatChip icon="🟡"  label="Khác"       count={cleaning}  color="var(--warning)" />
      </div>

      {/* Bed grid */}
      <div
        className="overflow-y-auto flex-1 min-h-0"
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(90px, 1fr))',
          gap: '6px',
          alignContent: 'start',
        }}
      >
        {rows.map(bed => (
          <BedCell key={bed.id} bed={bed} />
        ))}
      </div>
    </div>
  );
}
