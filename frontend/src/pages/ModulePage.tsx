import { useParams } from 'react-router-dom';
import { useEffect, useState, useCallback, useRef } from 'react';
import { apiGet } from '../api/client';
import type { ModuleLayout, ModuleTab } from '../types/module';
import { LiveWidget } from '../components/widgets/LiveWidget';
import { WidgetErrorBoundary } from '../components/widgets/WidgetErrorBoundary';

// ── ModulePage ─────────────────────────────────────────────────────────────────
// Config-driven page renderer for /m/:slug routes.
// Fetches layout from GET /api/v1/modules/:slug/layout and renders tabs + widgets.

export function ModulePage() {
  const { slug } = useParams<{ slug: string }>();
  const [layout, setLayout]   = useState<ModuleLayout | null>(null);
  const [activeTab, setActiveTab] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState<string | null>(null);

  // Page-level filter state — passed down to every widget as ResolveContext.filters.
  // Widgets whose paramsTemplate references {{filters.x}} pick these up automatically.
  const [filters, setFilters] = useState<Record<string, unknown>>({});

  // Auto-refresh tick — incremented every refreshIntervalSeconds to re-fetch all widgets.
  const [refreshTick, setRefreshTick] = useState(0);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const updateFilter = useCallback((key: string, value: unknown) => {
    setFilters(prev => ({ ...prev, [key]: value }));
  }, []);

  useEffect(() => {
    if (!slug) return;
    setLoading(true);
    setError(null);
    setLayout(null);
    setActiveTab(null);
    setFilters({});
    setRefreshTick(0);
    if (intervalRef.current) clearInterval(intervalRef.current);

    apiGet<ModuleLayout>(`/api/v1/modules/${slug}/layout`)
      .then(data => {
        setLayout(data);
        const def = data.tabs.find(t => t.isDefault) ?? data.tabs[0];
        setActiveTab(def?.slug ?? null);

        // Start auto-refresh if the module has an interval configured.
        if (data.refreshIntervalSeconds && data.refreshIntervalSeconds > 0) {
          intervalRef.current = setInterval(
            () => setRefreshTick(t => t + 1),
            data.refreshIntervalSeconds * 1000,
          );
        }
      })
      .catch(err => setError((err as Error)?.message ?? 'Failed to load module'))
      .finally(() => setLoading(false));

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [slug]);

  if (loading) return <ModulePageSkeleton />;
  if (error)   return <ModulePageError message={error} />;
  if (!layout || layout.tabs.length === 0) return <ModulePageEmpty />;

  const currentTab = layout.tabs.find(t => t.slug === activeTab) ?? layout.tabs[0];

  return (
    <div className="flex flex-col h-full">
      {/* Tab bar — only render if more than 1 tab */}
      {layout.tabs.length > 1 && (
        <div className="flex gap-1 px-1 pb-3 border-b border-[--border] flex-shrink-0">
          {layout.tabs.map(tab => (
            <button
              key={tab.slug}
              onClick={() => setActiveTab(tab.slug)}
              className={[
                'px-4 py-2 rounded-lg text-sm font-medium transition-colors',
                tab.slug === activeTab
                  ? 'bg-[--brand] text-white'
                  : 'text-[--tx2] hover:text-[--tx] hover:bg-[--overlay]',
              ].join(' ')}
            >
              {tab.label}
            </button>
          ))}
        </div>
      )}

      {/* Widget canvas */}
      <div className="flex-1 overflow-y-auto pt-4">
        <WidgetCanvas tab={currentTab} filters={filters} onFilterChange={updateFilter} refreshTick={refreshTick} />
      </div>
    </div>
  );
}

// ── Widget Canvas ─────────────────────────────────────────────────────────────

interface WidgetCanvasProps {
  tab:            ModuleTab;
  filters:        Record<string, unknown>;
  onFilterChange: (key: string, value: unknown) => void;
  refreshTick:    number;
}

function WidgetCanvas({ tab, filters, onFilterChange, refreshTick }: WidgetCanvasProps) {
  if (tab.widgets.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-64 text-[--tx3]">
        <p className="text-sm">Chưa có widget nào.</p>
        <p className="text-xs mt-1">Vào Admin → Dashboard Designer để thêm widget.</p>
      </div>
    );
  }

  return (
    <div
      className="grid gap-4 px-1 pb-6"
      style={{ gridTemplateColumns: 'repeat(12, 1fr)' }}
    >
      {tab.widgets.map(widget => (
        <WidgetErrorBoundary key={widget.widgetKey} widgetKey={widget.widgetKey}>
          <LiveWidget
            widget={widget}
            filters={filters}
            onFilterChange={onFilterChange}
            refreshTick={refreshTick}
            style={{
              gridColumn: `${widget.gridX + 1} / span ${widget.gridW}`,
              gridRow:    `${widget.gridY + 1} / span ${widget.gridH}`,
            }}
          />
        </WidgetErrorBoundary>
      ))}
    </div>
  );
}

// ── Loading / error states ────────────────────────────────────────────────────

function ModulePageSkeleton() {
  return (
    <div className="p-6 space-y-4">
      <div className="hdos-skeleton h-10 w-64 rounded-lg" />
      <div className="grid grid-cols-4 gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="hdos-skeleton h-24 rounded-xl" />
        ))}
      </div>
      <div className="hdos-skeleton h-48 rounded-xl" />
    </div>
  );
}

function ModulePageError({ message }: { message: string }) {
  return (
    <div className="flex flex-col items-center justify-center h-64 gap-2">
      <span className="text-2xl">⚠️</span>
      <p className="text-sm text-[--danger]">{message}</p>
    </div>
  );
}

function ModulePageEmpty() {
  return (
    <div className="flex flex-col items-center justify-center h-64 text-[--tx3]">
      <p className="text-sm">Module không có tab nào.</p>
    </div>
  );
}
