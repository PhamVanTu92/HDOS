import { useState } from 'react';

interface RoomItem {
  id:        string;
  label:     string;
  type?:     'ward' | 'icu' | 'or' | 'er' | 'recovery' | string;
  status:    'available' | 'occupied' | 'cleaning' | 'maintenance' | 'reserved' | string;
  capacity?: number;
  occupancy?: number;
  floor?:    string | number;
  wing?:     string;
}

interface RoomStatusData {
  rows:   RoomItem[];
  title?: string;
}

const TYPE_LABEL: Record<string, string> = {
  ward:     'Nội trú',
  icu:      'Hồi sức',
  or:       'Phẫu thuật',
  er:       'Cấp cứu',
  recovery: 'Hồi phục',
};

const STATUS_BORDER: Record<string, string> = {
  available:   '2px solid var(--success)',
  occupied:    '2px solid var(--danger)',
  cleaning:    '2px solid var(--info)',
  maintenance: '2px solid var(--warning)',
  reserved:    '2px solid var(--warning)',
};

const STATUS_BG: Record<string, string> = {
  available:   'var(--success-bg)',
  occupied:    'var(--danger-bg)',
  cleaning:    'var(--info-bg)',
  maintenance: 'var(--warning-bg)',
  reserved:    'var(--warning-bg)',
};

const STATUS_TEXT_COLOR: Record<string, string> = {
  available:   'var(--success)',
  occupied:    'var(--danger)',
  cleaning:    'var(--info)',
  maintenance: 'var(--warning)',
  reserved:    'var(--warning)',
};

const STATUS_DISPLAY: Record<string, string> = {
  available:   'Trống',
  occupied:    'Đang dùng',
  cleaning:    'Đang dọn',
  maintenance: 'Bảo trì',
  reserved:    'Giữ chỗ',
};

type FilterKey = 'all' | 'available' | 'occupied' | 'cleaning';

const FILTER_TABS: { key: FilterKey; label: string }[] = [
  { key: 'all',       label: 'Tất cả' },
  { key: 'available', label: 'Trống' },
  { key: 'occupied',  label: 'Đang dùng' },
  { key: 'cleaning',  label: 'Đang dọn' },
];

function OccupancyBar({ occupancy, capacity }: { occupancy: number; capacity: number }) {
  const ratio = capacity > 0 ? occupancy / capacity : 0;
  const pct   = Math.min(ratio * 100, 100);
  const color =
    ratio > 0.9 ? 'var(--danger)' :
    ratio > 0.7 ? 'var(--warning)' :
                  'var(--success)';

  return (
    <div className="mt-1.5">
      <div
        className="flex justify-between mb-0.5"
        style={{ fontSize: '10px', color: 'var(--tx3)' }}
      >
        <span>{occupancy}/{capacity}</span>
        <span>{Math.round(pct)}%</span>
      </div>
      <div
        className="w-full rounded-full overflow-hidden"
        style={{ height: '4px', background: 'var(--border)' }}
      >
        <div
          className="h-full rounded-full transition-all duration-500"
          style={{ width: `${pct}%`, background: color }}
        />
      </div>
    </div>
  );
}

function RoomCard({ room }: { room: RoomItem }) {
  const border     = STATUS_BORDER[room.status]    ?? '2px solid var(--border)';
  const background = STATUS_BG[room.status]        ?? 'var(--card)';
  const textColor  = STATUS_TEXT_COLOR[room.status] ?? 'var(--tx3)';
  const typeLabel  = room.type ? (TYPE_LABEL[room.type] ?? room.type) : null;
  const hasCapacity = room.capacity != null && room.occupancy != null;

  return (
    <div
      className="rounded-lg p-2.5 flex flex-col gap-1 overflow-hidden"
      style={{ border, background }}
    >
      {/* Room label + type badge */}
      <div className="flex items-start justify-between gap-1 min-w-0">
        <p
          className="text-sm font-bold leading-tight truncate"
          style={{ color: 'var(--tx)' }}
        >
          {room.label}
        </p>
        {typeLabel && (
          <span
            className="shrink-0 rounded px-1.5 py-0.5 leading-none"
            style={{
              fontSize: '9px',
              background: 'rgba(255,255,255,0.07)',
              color: 'var(--tx2)',
              border: '1px solid var(--border)',
              whiteSpace: 'nowrap',
            }}
          >
            {typeLabel}
          </span>
        )}
      </div>

      {/* Floor / wing info */}
      {(room.floor != null || room.wing) && (
        <p className="text-[10px] leading-tight" style={{ color: 'var(--tx3)' }}>
          {room.floor != null && `Tầng ${room.floor}`}
          {room.floor != null && room.wing && ' · '}
          {room.wing}
        </p>
      )}

      {/* Occupancy bar */}
      {hasCapacity && (
        <OccupancyBar occupancy={room.occupancy!} capacity={room.capacity!} />
      )}

      {/* Status text */}
      <p
        className="text-[10px] font-semibold leading-tight mt-auto"
        style={{ color: textColor }}
      >
        {STATUS_DISPLAY[room.status] ?? room.status}
      </p>
    </div>
  );
}

export function RoomStatusGridWidget({ data }: { data: unknown }) {
  const [activeFilter, setActiveFilter] = useState<FilterKey>('all');

  const d = data as RoomStatusData | null;
  if (!d?.rows?.length) {
    return (
      <div className="flex items-center justify-center h-full text-sm" style={{ color: 'var(--tx3)' }}>
        Không có dữ liệu phòng
      </div>
    );
  }

  const rows = d.rows;

  const filtered =
    activeFilter === 'all'
      ? rows
      : rows.filter(r => r.status === activeFilter);

  const totalCount     = rows.length;
  const availableCount = rows.filter(r => r.status === 'available').length;
  const occupiedCount  = rows.filter(r => r.status === 'occupied').length;

  return (
    <div className="flex flex-col h-full gap-3 min-h-0">
      {/* Filter tabs */}
      <div className="flex items-center gap-2 flex-wrap shrink-0">
        {FILTER_TABS.map(tab => {
          const isActive = activeFilter === tab.key;
          return (
            <button
              key={tab.key}
              onClick={() => setActiveFilter(tab.key)}
              className="rounded-full px-2.5 py-0.5 text-xs font-medium leading-5 transition-colors"
              style={{
                background: isActive ? 'var(--brand)' : 'var(--surface)',
                color:      isActive ? '#fff' : 'var(--tx2)',
                border:     isActive ? '1px solid var(--brand)' : '1px solid var(--border)',
                cursor: 'pointer',
              }}
            >
              {tab.label}
            </button>
          );
        })}
      </div>

      {/* Summary line */}
      <p className="text-xs shrink-0" style={{ color: 'var(--tx2)' }}>
        <span style={{ color: 'var(--tx)', fontWeight: 600 }}>{totalCount}</span>
        {' phòng'}
        {' · '}
        <span style={{ color: 'var(--success)', fontWeight: 600 }}>{availableCount}</span>
        {' trống'}
        {' · '}
        <span style={{ color: 'var(--danger)', fontWeight: 600 }}>{occupiedCount}</span>
        {' đang dùng'}
      </p>

      {/* Room grid */}
      {filtered.length === 0 ? (
        <div className="flex-1 flex items-center justify-center text-sm" style={{ color: 'var(--tx3)' }}>
          Không có phòng nào
        </div>
      ) : (
        <div
          className="overflow-y-auto flex-1 min-h-0"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(140px, 1fr))',
            gap: '8px',
            alignContent: 'start',
          }}
        >
          {filtered.map(room => (
            <RoomCard key={room.id} room={room} />
          ))}
        </div>
      )}
    </div>
  );
}
