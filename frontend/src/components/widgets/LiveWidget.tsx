import { type CSSProperties, useEffect } from 'react';
import { useWidgetData } from '../../hooks/useWidgetData';
import { WidgetRenderer, FILTER_CHART_TYPES, type FilterProps } from './WidgetRenderer';
import type { WidgetLayout } from '../../types/module';
import type { ResolveContext } from '../../utils/templateResolver';

interface LiveWidgetProps {
  widget:    WidgetLayout;
  /** Page-level filter values forwarded to template resolver */
  filters?:  Record<string, unknown>;
  className?: string;
  /** Grid placement — applied directly to the card wrapper */
  style?:    CSSProperties;
  /** Called by filter-type widgets to write back to page-level filter state */
  onFilterChange?: (key: string, value: unknown) => void;
  /**
   * Auto-refresh tick — incremented by ModulePage every N seconds.
   * When this changes, the widget re-fetches its data.
   */
  refreshTick?: number;
}

export function LiveWidget({
  widget,
  filters = {},
  className = '',
  style,
  onFilterChange,
  refreshTick,
}: LiveWidgetProps) {
  const ctx: ResolveContext = { filters };
  const { status, data, error, refresh } = useWidgetData(widget, ctx);

  // Trigger a refresh whenever the page-level auto-refresh tick increments.
  // Skip the initial mount (tick === 0 or undefined) — useWidgetData fetches on mount.
  useEffect(() => {
    if (refreshTick && refreshTick > 0) refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshTick]);

  const isFilter = FILTER_CHART_TYPES.has(widget.chartType);

  // Build filter props only when the widget is a filter control and has a filterKey
  const filterProps: FilterProps | undefined =
    isFilter && widget.filterKey && onFilterChange
      ? {
          filterKey:      widget.filterKey,
          currentFilters: filters,
          onFilterChange,
        }
      : undefined;

  return (
    <div className={`hdos-card flex flex-col overflow-hidden h-full ${className}`} style={style}>

      {/* ── Header ─────────────────────────────────────────────────────────── */}
      {/* Filter widgets show a compact header (no refresh/spinner clutter)    */}
      <div className="widget-header flex-shrink-0">
        <div className="min-w-0">
          <p className="widget-title truncate">{widget.title ?? widget.widgetKey}</p>
          {widget.subtitle && (
            <p className="widget-subtitle mt-0.5 truncate">{widget.subtitle}</p>
          )}
        </div>

        {!isFilter && (
          <div className="flex items-center gap-2 shrink-0">
            {status === 'loading' && (
              <span className="w-3 h-3 rounded-full border-2 border-[--brand] border-t-transparent animate-spin" />
            )}
            {status === 'done' && widget.operationPattern && (
              <button
                onClick={refresh}
                title="Làm mới"
                className="text-[--tx3] hover:text-[--tx2] transition-colors text-sm leading-none"
              >
                ↺
              </button>
            )}
          </div>
        )}
      </div>

      {/* ── Body ───────────────────────────────────────────────────────────── */}
      <div className="flex-1 min-h-0 px-4 pb-4">

        {/* Loading skeleton — only for data widgets */}
        {!isFilter && status === 'loading' && <WidgetLoadingSkeleton />}

        {/* Error state */}
        {status === 'error' && (
          <div className="flex flex-col items-center justify-center h-full gap-2">
            <p className="text-xs text-[--danger] text-center">{error}</p>
            <button onClick={refresh} className="btn-ghost text-[10px]">Thử lại</button>
          </div>
        )}

        {/* No operation configured — only for data widgets */}
        {!isFilter && status === 'idle' && !widget.operationPattern && (
          <div className="flex items-center justify-center h-full">
            <p className="text-xs text-[--tx3] italic">Chưa cấu hình operation</p>
          </div>
        )}

        {/* Filter widgets always render (they don't need an operation)       */}
        {/* Data widgets render when status === 'done'                        */}
        {(isFilter || status === 'done') && (
          <WidgetRenderer
            chartType={widget.chartType}
            data={data}
            visualConfig={widget.visualConfig}
            filter={filterProps}
          />
        )}
      </div>
    </div>
  );
}

function WidgetLoadingSkeleton() {
  return (
    <div className="flex flex-col gap-2 h-full animate-pulse">
      <div className="h-4 w-3/4 rounded bg-white/10" />
      <div className="flex-1 rounded bg-white/5" />
    </div>
  );
}
