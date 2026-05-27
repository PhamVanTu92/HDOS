import { useEffect, useRef, useState } from 'react';

interface BaseFilterProps {
  data:           unknown;
  visualConfig?:  string;
  filterKey:      string;
  currentFilters: Record<string, unknown>;
  onFilterChange: (key: string, value: unknown) => void;
}

interface SearchConfig {
  placeholder?: string;
  debounceMs?:  number;
  minLength?:   number;
}

// ── Config parser ─────────────────────────────────────────────────────────────

function parseSearchConfig(visualConfig?: string): SearchConfig {
  if (!visualConfig) return {};
  try {
    return JSON.parse(visualConfig) as SearchConfig;
  } catch {
    return {};
  }
}

// ── Component ─────────────────────────────────────────────────────────────────

export function FilterSearchWidget({
  visualConfig,
  filterKey,
  currentFilters,
  onFilterChange,
}: BaseFilterProps) {
  const config    = parseSearchConfig(visualConfig);
  const debounceMs = config.debounceMs  ?? 500;
  const minLength  = config.minLength   ?? 0;
  const placeholder = config.placeholder ?? 'Tìm kiếm...';

  const externalValue = currentFilters[filterKey];
  const [inputValue, setInputValue] = useState<string>(
    typeof externalValue === 'string' ? externalValue : '',
  );

  // Track whether a state update came from external sync so we can skip debounce
  const skipDebounceRef = useRef<boolean>(false);
  const timerRef        = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Sync when external filter value changes (e.g. global reset)
  useEffect(() => {
    const ext = currentFilters[filterKey];
    const next = typeof ext === 'string' ? ext : '';
    setInputValue((prev) => {
      if (prev === next) return prev;
      skipDebounceRef.current = true;
      return next;
    });
  }, [currentFilters, filterKey]);

  // ── Debounced emit ────────────────────────────────────────────────────────

  function scheduleEmit(value: string) {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
    }
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      if (value === '') {
        onFilterChange(filterKey, null);
      } else if (value.length >= minLength) {
        onFilterChange(filterKey, value);
      }
    }, debounceMs);
  }

  // ── Handlers ──────────────────────────────────────────────────────────────

  function handleChange(value: string) {
    setInputValue(value);

    if (skipDebounceRef.current) {
      skipDebounceRef.current = false;
      return;
    }

    scheduleEmit(value);
  }

  function handleClear() {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    setInputValue('');
    onFilterChange(filterKey, null);
  }

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (timerRef.current !== null) clearTimeout(timerRef.current);
    };
  }, []);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="flex items-center h-full">
      <div className="relative flex-1">
        {/* Magnifying glass icon */}
        <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[--tx3] pointer-events-none">
          <svg
            width="14"
            height="14"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <circle cx="11" cy="11" r="8" strokeWidth={2} />
            <path d="M21 21l-4.35-4.35" strokeWidth={2} strokeLinecap="round" />
          </svg>
        </span>

        <input
          type="text"
          value={inputValue}
          onChange={(e) => handleChange(e.target.value)}
          placeholder={placeholder}
          style={{
            width:        '100%',
            background:   'var(--overlay)',
            color:        'var(--tx)',
            border:       '1px solid var(--border)',
            borderRadius: 6,
            padding:      '6px 32px 6px 30px',
            fontSize:     13,
            outline:      'none',
          }}
          onFocus={(e) => {
            e.currentTarget.style.borderColor = 'var(--brand)';
          }}
          onBlur={(e) => {
            e.currentTarget.style.borderColor = 'var(--border)';
          }}
        />

        {/* Clear button */}
        {inputValue.length > 0 && (
          <button
            onClick={handleClear}
            className="absolute right-2 top-1/2 -translate-y-1/2 text-[--tx3] hover:text-[--tx] transition-colors"
            aria-label="Xóa tìm kiếm"
          >
            ×
          </button>
        )}
      </div>
    </div>
  );
}
