interface BaseFilterProps {
  data:           unknown;
  visualConfig?:  string;
  filterKey:      string;
  currentFilters: Record<string, unknown>;
  onFilterChange: (key: string, value: unknown) => void;
}

interface DropdownOption {
  label: string;
  value: string;
}

interface DropdownConfig {
  options?:     DropdownOption[];
  placeholder?: string;
  multiSelect?: boolean;
  allowClear?:  boolean;
}

function parseDropdownConfig(visualConfig?: string): DropdownConfig {
  if (!visualConfig) return {};
  try {
    return JSON.parse(visualConfig) as DropdownConfig;
  } catch {
    return {};
  }
}

function isDropdownOptions(val: unknown): val is DropdownOption[] {
  return (
    Array.isArray(val) &&
    val.every(
      (o) =>
        o !== null &&
        typeof o === 'object' &&
        'label' in o &&
        'value' in o &&
        typeof (o as Record<string, unknown>).label === 'string' &&
        typeof (o as Record<string, unknown>).value === 'string',
    )
  );
}

function resolveOptions(data: unknown, config: DropdownConfig): DropdownOption[] {
  // Prefer data.options over visualConfig.options
  if (
    data !== null &&
    typeof data === 'object' &&
    'options' in data &&
    isDropdownOptions((data as Record<string, unknown>).options)
  ) {
    return (data as { options: DropdownOption[] }).options;
  }
  return config.options ?? [];
}

export function FilterDropdownWidget({
  data,
  visualConfig,
  filterKey,
  currentFilters,
  onFilterChange,
}: BaseFilterProps) {
  const config      = parseDropdownConfig(visualConfig);
  const options     = resolveOptions(data, config);
  const multiSelect = config.multiSelect ?? false;
  const allowClear  = config.allowClear ?? true;
  const placeholder = config.placeholder ?? 'Tất cả';

  const rawValue = currentFilters[filterKey];

  // ── Multi-select mode ────────────────────────────────────────────────────
  if (multiSelect) {
    const currentValues: string[] = Array.isArray(rawValue)
      ? (rawValue as string[])
      : rawValue != null
        ? [String(rawValue)]
        : [];

    function toggle(val: string) {
      const next = currentValues.includes(val)
        ? currentValues.filter((v) => v !== val)
        : [...currentValues, val];
      onFilterChange(filterKey, next.length > 0 ? next : null);
    }

    // Loading skeleton — no options yet and data is still null
    if (options.length === 0 && data === null) {
      return (
        <div className="flex flex-wrap gap-1.5 content-start overflow-y-auto h-full py-1 animate-pulse">
          {[80, 64, 96, 72].map((w) => (
            <div
              key={w}
              style={{ width: w, height: 26, background: 'var(--overlay)', borderRadius: 4 }}
            />
          ))}
        </div>
      );
    }

    if (options.length === 0) {
      return (
        <p className="text-xs text-[--tx3] italic">Không có tùy chọn</p>
      );
    }

    return (
      <div className="flex flex-wrap gap-1.5 content-start overflow-y-auto h-full py-1">
        {options.map((opt) => {
          const isSelected = currentValues.includes(opt.value);
          return (
            <button
              key={opt.value}
              onClick={() => toggle(opt.value)}
              className={`px-2.5 py-1 rounded text-xs font-medium border transition-colors ${
                isSelected
                  ? 'bg-[--brand] border-[--brand] text-white'
                  : 'bg-transparent border-[--border] text-[--tx2] hover:border-[--brand] hover:text-[--tx]'
              }`}
            >
              {opt.label}
            </button>
          );
        })}
      </div>
    );
  }

  // ── Single-select mode ───────────────────────────────────────────────────
  const currentValue =
    rawValue != null && rawValue !== '' ? String(rawValue) : null;

  function handleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const val = e.target.value;
    onFilterChange(filterKey, val !== '' ? val : null);
  }

  // Loading skeleton
  if (options.length === 0 && data === null) {
    return (
      <div className="flex flex-col h-full justify-center gap-1 animate-pulse">
        <div
          style={{
            height: 34,
            background: 'var(--overlay)',
            borderRadius: 6,
            width: '100%',
          }}
        />
      </div>
    );
  }

  if (options.length === 0) {
    return (
      <div className="flex flex-col h-full justify-center gap-1">
        <p className="text-xs text-[--tx3] italic">Không có tùy chọn</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full justify-center gap-1">
      <select
        value={currentValue ?? ''}
        onChange={handleChange}
        style={{
          background:   'var(--overlay)',
          color:        'var(--tx)',
          border:       '1px solid var(--border)',
          borderRadius: '6px',
          padding:      '6px 10px',
          fontSize:     '13px',
          outline:      'none',
          width:        '100%',
          cursor:       'pointer',
        }}
      >
        {allowClear && <option value="">{placeholder}</option>}
        {options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </div>
  );
}
