interface MapPin {
  id:      string;
  label:   string;
  lat?:    number;
  lng?:    number;
  x?:      number;
  y?:      number;
  value?:  number | null;
  status?: 'ok' | 'warning' | 'critical' | 'info' | string;
  color?:  string | null;
}

interface MapData {
  rows:    MapPin[];
  title?:  string;
  bounds?: { minLat: number; maxLat: number; minLng: number; maxLng: number };
}

const PADDING = 20;
const W = 400;
const H = 225;

function pinColor(status?: string): string {
  switch (status) {
    case 'ok':       return 'var(--success)';
    case 'warning':  return 'var(--warning)';
    case 'critical': return 'var(--danger)';
    case 'info':     return 'var(--info)';
    default:         return 'var(--brand)';
  }
}

type NormalizedPin = MapPin & { svgX: number; svgY: number };

function normalizeCoords(pins: MapPin[]): NormalizedPin[] {
  const hasXY = pins.some(p => p.x !== undefined);

  if (hasXY) {
    return pins.map(p => ({
      ...p,
      svgX: PADDING + ((p.x ?? 50) / 100) * (W - 2 * PADDING),
      svgY: PADDING + (1 - (p.y ?? 50) / 100) * (H - 2 * PADDING),
    }));
  }

  const lats = pins.map(p => p.lat ?? 0);
  const lngs = pins.map(p => p.lng ?? 0);
  const minLat = Math.min(...lats);
  const maxLat = Math.max(...lats);
  const minLng = Math.min(...lngs);
  const maxLng = Math.max(...lngs);
  const latRange = maxLat - minLat || 1;
  const lngRange = maxLng - minLng || 1;

  return pins.map(p => ({
    ...p,
    svgX: PADDING + (((p.lng ?? minLng) - minLng) / lngRange) * (W - 2 * PADDING),
    svgY: PADDING + (1 - ((p.lat ?? minLat) - minLat) / latRange) * (H - 2 * PADDING),
  }));
}

const GRID_LINES = [1, 2, 3, 4].map(i => ({
  x1: PADDING + (i / 5) * (W - 2 * PADDING),
  x2: PADDING + (i / 5) * (W - 2 * PADDING),
  y1: PADDING,
  y2: H - PADDING,
})).concat(
  [1, 2, 3].map(i => ({
    x1: PADDING,
    x2: W - PADDING,
    y1: PADDING + (i / 4) * (H - 2 * PADDING),
    y2: PADDING + (i / 4) * (H - 2 * PADDING),
  }))
);

const LEGEND_STATUSES: Array<{ status: 'ok' | 'warning' | 'critical' | 'info'; label: string }> = [
  { status: 'ok',       label: 'Bình thường' },
  { status: 'warning',  label: 'Cảnh báo' },
  { status: 'critical', label: 'Nguy kịch' },
  { status: 'info',     label: 'Thông tin' },
];

export function MapPinsWidget({ data }: { data: unknown }) {
  const d = data as MapData | null;
  const rows: MapPin[] = d?.rows ?? [];

  if (!rows.length) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-1">
        <span className="text-2xl">📍</span>
        <p className="text-sm text-[--tx3]">Không có dữ liệu vị trí</p>
      </div>
    );
  }

  if (rows.length === 1) {
    const pin = rows[0];
    return (
      <div className="flex flex-col items-center justify-center h-full gap-2 p-4">
        <p className="text-xs text-[--tx3]">Cần ít nhất 2 điểm để hiển thị bản đồ</p>
        <div
          className="flex flex-col gap-1 p-3 rounded-lg w-full max-w-xs"
          style={{ background: 'var(--card)', border: '1px solid var(--border)' }}
        >
          <div className="flex items-center gap-2">
            <span
              className="w-3 h-3 rounded-full shrink-0"
              style={{ background: pinColor(pin.status) }}
            />
            <span className="text-sm font-medium text-[--tx]">{pin.label}</span>
          </div>
          {pin.value != null && (
            <p className="text-xs text-[--tx2] pl-5">Giá trị: <span className="font-bold text-[--tx]">{pin.value}</span></p>
          )}
          {(pin.lat != null || pin.x != null) && (
            <p className="text-[10px] text-[--tx3] pl-5">
              {pin.x != null ? `x: ${pin.x}, y: ${pin.y}` : `lat: ${pin.lat}, lng: ${pin.lng}`}
            </p>
          )}
        </div>
      </div>
    );
  }

  const normalizedPins = normalizeCoords(rows);
  const presentStatuses = new Set(rows.map(p => p.status ?? 'default'));

  return (
    <div className="flex flex-col h-full gap-2">
      {d?.title && (
        <p className="text-xs font-medium text-[--tx2] shrink-0">{d.title}</p>
      )}

      {/* SVG map area */}
      <div
        className="relative w-full shrink-0"
        style={{ aspectRatio: '16/9', background: 'var(--card)', borderRadius: 8, overflow: 'hidden' }}
      >
        <svg
          width="100%"
          height="100%"
          viewBox={`0 0 ${W} ${H}`}
          preserveAspectRatio="xMidYMid meet"
          style={{ display: 'block' }}
        >
          {/* Grid lines */}
          {GRID_LINES.map((line, i) => (
            <line
              key={i}
              x1={line.x1} y1={line.y1}
              x2={line.x2} y2={line.y2}
              stroke="var(--border)"
              strokeWidth={0.5}
              strokeDasharray="3,3"
            />
          ))}

          {/* Outer boundary */}
          <rect
            x={PADDING} y={PADDING}
            width={W - 2 * PADDING} height={H - 2 * PADDING}
            fill="none"
            stroke="var(--border-md)"
            strokeWidth={0.75}
            rx={2}
          />

          {/* Pins */}
          {normalizedPins.map(pin => {
            const r = pin.value != null
              ? Math.min(10, 4 + Math.sqrt(Math.abs(pin.value)))
              : 6;
            const color = pin.color ?? pinColor(pin.status);

            return (
              <g key={pin.id} transform={`translate(${pin.svgX}, ${pin.svgY})`}>
                {/* Glow ring */}
                <circle
                  r={r + 3}
                  fill={color}
                  opacity={0.15}
                />
                {/* Pin circle */}
                <circle
                  r={r}
                  fill={color}
                  opacity={0.85}
                  stroke="var(--surface)"
                  strokeWidth={1.5}
                />
                {/* Label */}
                <text
                  y={-(r + 5)}
                  textAnchor="middle"
                  fontSize={8}
                  fill="var(--tx2)"
                  style={{ pointerEvents: 'none', userSelect: 'none' }}
                >
                  {pin.label}
                </text>
                {/* Value */}
                {pin.value != null && (
                  <text
                    y={r > 5 ? 3.5 : 4}
                    textAnchor="middle"
                    fontSize={7}
                    fill="var(--tx)"
                    fontWeight="bold"
                    style={{ pointerEvents: 'none', userSelect: 'none' }}
                  >
                    {pin.value}
                  </text>
                )}
              </g>
            );
          })}
        </svg>
      </div>

      {/* Legend */}
      <div className="flex flex-wrap gap-x-4 gap-y-1 shrink-0">
        {LEGEND_STATUSES.filter(ls =>
          presentStatuses.has(ls.status) || presentStatuses.has('default')
        ).map(ls => (
          <div key={ls.status} className="flex items-center gap-1.5">
            <span
              className="w-2 h-2 rounded-full shrink-0"
              style={{ background: pinColor(ls.status) }}
            />
            <span className="text-[10px] text-[--tx3]">{ls.label}</span>
          </div>
        ))}
        {/* "default" brand color entry if any pins have no status */}
        {presentStatuses.has('default') || [...presentStatuses].some(s => !['ok','warning','critical','info'].includes(s)) ? (
          <div className="flex items-center gap-1.5">
            <span className="w-2 h-2 rounded-full shrink-0" style={{ background: 'var(--brand)' }} />
            <span className="text-[10px] text-[--tx3]">Khác</span>
          </div>
        ) : null}
      </div>
    </div>
  );
}
