interface BaseFilterProps {
  data:           unknown;
  visualConfig?:  string;
  filterKey:      string;
  currentFilters: Record<string, unknown>;
  onFilterChange: (key: string, value: unknown) => void;
}

interface SliderConfig {
  min?:    number;
  max?:    number;
  step?:   number;
  format?: 'number' | 'percent' | 'currency';
  label?:  string;
  mode?:   string;
}

interface RangeValue {
  min: number;
  max: number;
}

function parseSliderConfig(visualConfig?: string): SliderConfig {
  if (!visualConfig) return {};
  try {
    return JSON.parse(visualConfig) as SliderConfig;
  } catch {
    return {};
  }
}

/** Merge API data fields over visualConfig fields (data takes priority). */
function resolveConfig(data: unknown, config: SliderConfig): SliderConfig {
  if (data !== null && typeof data === 'object') {
    const d = data as Record<string, unknown>;
    return {
      min:    typeof d.min    === 'number' ? d.min    : config.min,
      max:    typeof d.max    === 'number' ? d.max    : config.max,
      step:   typeof d.step   === 'number' ? d.step   : config.step,
      format: typeof d.format === 'string' ? (d.format as SliderConfig['format']) : config.format,
      label:  typeof d.label  === 'string' ? d.label  : config.label,
      mode:   typeof d.mode   === 'string' ? d.mode   : config.mode,
    };
  }
  return config;
}

function formatVal(n: number, format?: string): string {
  if (format === 'percent')  return `${n}%`;
  if (format === 'currency') return n.toLocaleString('vi-VN') + ' ₫';
  return n.toLocaleString('vi-VN');
}

function isRangeValue(val: unknown): val is RangeValue {
  return (
    val !== null &&
    typeof val === 'object' &&
    'min' in val &&
    'max' in val &&
    typeof (val as Record<string, unknown>).min === 'number' &&
    typeof (val as Record<string, unknown>).max === 'number'
  );
}

export function FilterSliderWidget({
  data,
  visualConfig,
  filterKey,
  currentFilters,
  onFilterChange,
}: BaseFilterProps) {
  const rawConfig = parseSliderConfig(visualConfig);
  const cfg       = resolveConfig(data, rawConfig);

  const min    = cfg.min  ?? 0;
  const max    = cfg.max  ?? 100;
  const step   = cfg.step ?? 1;
  const format = cfg.format;
  const label  = cfg.label;

  const rawValue = currentFilters[filterKey];

  // Detect range mode: explicit config OR current value is a range object
  const isRange = cfg.mode === 'range' || isRangeValue(rawValue);

  const inputStyle: React.CSSProperties = {
    background:   'var(--overlay)',
    color:        'var(--tx)',
    border:       '1px solid var(--border)',
    borderRadius: 4,
    padding:      '4px 6px',
    fontSize:     12,
    width:        '100%',
  };

  // ── Range mode ───────────────────────────────────────────────────────────
  if (isRange) {
    const rangeVal = isRangeValue(rawValue) ? rawValue : { min, max };
    const lo = rangeVal.min;
    const hi = rangeVal.max;

    return (
      <div className="flex flex-col gap-2 justify-center h-full px-1">
        {label && (
          <p className="text-[11px] text-[--tx2] font-medium">{label}</p>
        )}
        <div className="flex gap-2 items-center">
          <span className="text-[10px] text-[--tx3] shrink-0">Từ</span>
          <input
            type="number"
            min={min}
            max={max}
            step={step}
            value={lo}
            onChange={(e) =>
              onFilterChange(filterKey, { min: Number(e.target.value), max: hi })
            }
            style={inputStyle}
          />
          <span className="text-[10px] text-[--tx3] shrink-0">Đến</span>
          <input
            type="number"
            min={min}
            max={max}
            step={step}
            value={hi}
            onChange={(e) =>
              onFilterChange(filterKey, { min: lo, max: Number(e.target.value) })
            }
            style={inputStyle}
          />
        </div>
        <p className="text-[10px] text-[--tx3] text-center">
          {formatVal(lo, format)} – {formatVal(hi, format)}
        </p>
      </div>
    );
  }

  // ── Single value mode ────────────────────────────────────────────────────
  const currentNum: number =
    typeof rawValue === 'number' ? rawValue : min;

  return (
    <div className="flex flex-col gap-3 justify-center h-full px-1">
      {label && (
        <p className="text-[11px] text-[--tx2] font-medium">{label}</p>
      )}
      <div className="flex justify-between text-[10px] text-[--tx3]">
        <span>{formatVal(min, format)}</span>
        <span className="font-semibold text-[--tx] text-sm">
          {formatVal(currentNum, format)}
        </span>
        <span>{formatVal(max, format)}</span>
      </div>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={currentNum}
        onChange={(e) => onFilterChange(filterKey, Number(e.target.value))}
        style={{
          width:       '100%',
          accentColor: 'var(--brand)',
          cursor:      'pointer',
        }}
      />
    </div>
  );
}
