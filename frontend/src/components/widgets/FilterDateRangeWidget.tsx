import { useEffect, useState } from 'react';

interface BaseFilterProps {
  data:           unknown;
  visualConfig?:  string;
  filterKey:      string;
  currentFilters: Record<string, unknown>;
  onFilterChange: (key: string, value: unknown) => void;
}

type DateRangeValue = { from: string; to: string } | null;

// ── Helpers ───────────────────────────────────────────────────────────────────

function isoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function addDays(d: Date, n: number): Date {
  const r = new Date(d);
  r.setDate(r.getDate() + n);
  return r;
}

// ── Presets (all lambdas evaluated fresh at render time) ──────────────────────

interface Preset {
  label: string;
  from:  () => string;
  to:    () => string;
}

const PRESETS: Preset[] = [
  {
    label: 'Hôm nay',
    from:  () => isoDate(new Date()),
    to:    () => isoDate(new Date()),
  },
  {
    label: 'Hôm qua',
    from:  () => isoDate(addDays(new Date(), -1)),
    to:    () => isoDate(addDays(new Date(), -1)),
  },
  {
    label: '7 ngày qua',
    from:  () => isoDate(addDays(new Date(), -7)),
    to:    () => isoDate(new Date()),
  },
  {
    label: '30 ngày qua',
    from:  () => isoDate(addDays(new Date(), -30)),
    to:    () => isoDate(new Date()),
  },
  {
    label: 'Tháng này',
    from:  () => isoDate(new Date(new Date().getFullYear(), new Date().getMonth(), 1)),
    to:    () => isoDate(new Date()),
  },
  {
    label: 'Tháng trước',
    from:  () => {
      const n = new Date();
      return isoDate(new Date(n.getFullYear(), n.getMonth() - 1, 1));
    },
    to: () => {
      const n = new Date();
      return isoDate(new Date(n.getFullYear(), n.getMonth(), 0));
    },
  },
  {
    label: 'Quý này',
    from:  () => {
      const n = new Date();
      const q = Math.floor(n.getMonth() / 3);
      return isoDate(new Date(n.getFullYear(), q * 3, 1));
    },
    to: () => isoDate(new Date()),
  },
  {
    label: 'Năm nay',
    from:  () => isoDate(new Date(new Date().getFullYear(), 0, 1)),
    to:    () => isoDate(new Date()),
  },
];

// ── Helpers ───────────────────────────────────────────────────────────────────

const ISO_DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

function isValidIso(s: string): boolean {
  return ISO_DATE_RE.test(s);
}

function extractDateRange(raw: unknown): { from: string; to: string } | null {
  if (
    raw !== null &&
    raw !== undefined &&
    typeof raw === 'object' &&
    'from' in raw &&
    'to' in raw
  ) {
    const { from, to } = raw as { from: unknown; to: unknown };
    if (typeof from === 'string' && typeof to === 'string') {
      return { from, to };
    }
  }
  return null;
}

// ── Styles ────────────────────────────────────────────────────────────────────

const inputStyle: React.CSSProperties = {
  background:   'var(--overlay)',
  color:        'var(--tx)',
  border:       '1px solid var(--border)',
  borderRadius: 6,
  padding:      '4px 8px',
  fontSize:     12,
  colorScheme:  'dark',
};

function chipCls(active: boolean): string {
  const base =
    'px-2.5 py-1 rounded text-xs border transition-colors whitespace-nowrap cursor-pointer';
  return active
    ? `${base} bg-[--brand] border-[--brand] text-white`
    : `${base} bg-transparent border-[--border] text-[--tx2] hover:border-[--brand] hover:text-[--tx]`;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function FilterDateRangeWidget({
  filterKey,
  currentFilters,
  onFilterChange,
}: BaseFilterProps) {
  const externalValue = extractDateRange(currentFilters[filterKey]);

  const [localFrom, setLocalFrom] = useState<string>(externalValue?.from ?? '');
  const [localTo,   setLocalTo]   = useState<string>(externalValue?.to   ?? '');

  // Sync when an external change arrives (e.g. another widget resets this filter)
  useEffect(() => {
    const ext = extractDateRange(currentFilters[filterKey]);
    setLocalFrom(ext?.from ?? '');
    setLocalTo(ext?.to   ?? '');
  }, [currentFilters, filterKey]);

  // ── Derived active-preset detection (fresh at render) ──────────────────────
  function isActive(p: Preset): boolean {
    if (!localFrom || !localTo) return false;
    return localFrom === p.from() && localTo === p.to();
  }

  // ── Handlers ────────────────────────────────────────────────────────────────
  function applyPreset(p: Preset) {
    const from = p.from();
    const to   = p.to();
    setLocalFrom(from);
    setLocalTo(to);
    onFilterChange(filterKey, { from, to } satisfies DateRangeValue);
  }

  function handleFrom(value: string) {
    setLocalFrom(value);
    if (isValidIso(value) && isValidIso(localTo)) {
      onFilterChange(filterKey, { from: value, to: localTo } satisfies DateRangeValue);
    } else if (!value && !localTo) {
      onFilterChange(filterKey, null);
    }
  }

  function handleTo(value: string) {
    setLocalTo(value);
    if (isValidIso(localFrom) && isValidIso(value)) {
      onFilterChange(filterKey, { from: localFrom, to: value } satisfies DateRangeValue);
    } else if (!value && !localFrom) {
      onFilterChange(filterKey, null);
    }
  }

  // ── Render ──────────────────────────────────────────────────────────────────
  return (
    <div className="flex flex-col gap-2 h-full overflow-hidden">
      {/* Preset chips */}
      <div
        className="flex gap-1 overflow-x-auto pb-0.5 flex-shrink-0"
        style={{ scrollbarWidth: 'none' }}
      >
        {PRESETS.map((p) => (
          <button
            key={p.label}
            onClick={() => applyPreset(p)}
            className={chipCls(isActive(p))}
          >
            {p.label}
          </button>
        ))}
      </div>

      {/* Date inputs */}
      <div className="flex items-center gap-2 text-xs flex-shrink-0 flex-wrap">
        <span className="text-[--tx3] shrink-0">Từ</span>
        <input
          type="date"
          value={localFrom}
          onChange={(e) => handleFrom(e.target.value)}
          style={inputStyle}
        />
        <span className="text-[--tx3] shrink-0">Đến</span>
        <input
          type="date"
          value={localTo}
          onChange={(e) => handleTo(e.target.value)}
          style={inputStyle}
        />
      </div>
    </div>
  );
}
