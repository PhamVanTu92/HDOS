/**
 * DashboardDesigner — Trình thiết kế dashboard dạng kéo-thả
 * Route: /admin/modules/:slug/design
 *
 * Luồng:
 *  1. Load layout module từ GET /api/v1/modules/:slug/layout
 *  2. Load widget type catalog từ listWidgetSchemas()
 *  3. Hiển thị: header → tab selector → canvas (react-grid-layout) + right panel
 *  4. Lưu via saveWidgets(slug, tabId, widgets)
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { GridLayout } from 'react-grid-layout';
import type { Layout, LayoutItem } from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
import 'react-resizable/css/styles.css';
import { apiGet } from '../../api/client';
import { saveWidgets, listWidgetSchemas } from '../../api/adminModules';
import type {
  ModuleLayout,
  ModuleTab,
  WidgetChartType,
  WidgetTypeCatalogEntry,
  UpsertWidgetRequest,
  WidgetCategory,
} from '../../types/module';

// ── Internal State Type ────────────────────────────────────────────────────────

interface DesignerWidget {
  widgetKey:        string;
  title:            string;
  subtitle:         string;
  chartType:        string;
  gridX:            number;
  gridY:            number;
  gridW:            number;
  gridH:            number;
  operationPattern: string;
  providerId:       string;
  paramsTemplate:   string;
  visualConfig:     string;
  filterBindings:   string[];
  interactions:     string;
  filterKey:        string;
}

// ── Default sizes by chartType ─────────────────────────────────────────────────

const DEFAULT_SIZES: Record<string, { w: number; h: number }> = {
  kpi:                 { w: 3,  h: 2 },
  kpi_grid:            { w: 6,  h: 3 },
  line_chart:          { w: 6,  h: 4 },
  bar_chart:           { w: 6,  h: 4 },
  area_chart:          { w: 6,  h: 4 },
  pie_chart:           { w: 4,  h: 4 },
  donut_chart:         { w: 4,  h: 4 },
  simple_table:        { w: 8,  h: 5 },
  advanced_table:      { w: 12, h: 6 },
  progress_rows:       { w: 6,  h: 4 },
  alert_list:          { w: 6,  h: 5 },
  flow_steps:          { w: 12, h: 3 },
  patient_flow_stages: { w: 12, h: 4 },
  risk_tiers:          { w: 6,  h: 4 },
  bed_grid:            { w: 8,  h: 5 },
  room_status_grid:    { w: 6,  h: 4 },
  map_pins:            { w: 8,  h: 5 },
  timeline_vertical:   { w: 4,  h: 6 },
  news2_bars:          { w: 6,  h: 4 },
  chat_panel:          { w: 6,  h: 8 },
  filter_dropdown:     { w: 3,  h: 2 },
  filter_date_range:   { w: 4,  h: 2 },
  filter_slider:       { w: 3,  h: 2 },
  filter_search:       { w: 3,  h: 2 },
  text_widget:         { w: 6,  h: 2 },
  gauge:               { w: 4,  h: 4 },
  heatmap:             { w: 8,  h: 5 },
  funnel:              { w: 6,  h: 5 },
  scatter:             { w: 6,  h: 4 },
  pivot_table:         { w: 12, h: 6 },
};

// Thứ tự danh mục hiển thị trong catalog
const CATEGORY_ORDER: WidgetCategory[] = [
  'visualization',
  'healthcare',
  'filter',
  'layout',
  'ai',
];

const CATEGORY_LABELS: Record<WidgetCategory, string> = {
  visualization: 'Trực quan hóa',
  healthcare:    'Y tế',
  filter:        'Bộ lọc',
  layout:        'Bố cục',
  ai:            'AI',
};

// ── Helpers ────────────────────────────────────────────────────────────────────

/** Tạo widgetKey duy nhất */
function generateKey(chartType: string): string {
  return `${chartType}_${Date.now().toString(36)}`;
}

/** Chuyển WidgetLayout (từ API) sang DesignerWidget */
function fromApiWidget(w: ModuleTab['widgets'][number]): DesignerWidget {
  return {
    widgetKey:        w.widgetKey,
    title:            w.title        ?? '',
    subtitle:         w.subtitle     ?? '',
    chartType:        w.chartType,
    gridX:            w.gridX,
    gridY:            w.gridY,
    gridW:            w.gridW,
    gridH:            w.gridH,
    operationPattern: w.operationPattern ?? '',
    providerId:       w.providerId   ?? '',
    paramsTemplate:   w.paramsTemplate,
    visualConfig:     w.visualConfig,
    filterBindings:   w.filterBindings,
    interactions:     w.interactions,
    filterKey:        w.filterKey    ?? '',
  };
}

/** Chuyển DesignerWidget sang UpsertWidgetRequest để lưu */
function toUpsertRequest(w: DesignerWidget): UpsertWidgetRequest {
  return {
    widgetKey:        w.widgetKey,
    title:            w.title        || undefined,
    subtitle:         w.subtitle     || undefined,
    chartType:        w.chartType    as WidgetChartType,
    gridX:            w.gridX,
    gridY:            w.gridY,
    gridW:            w.gridW,
    gridH:            w.gridH,
    operationPattern: w.operationPattern || undefined,
    providerId:       w.providerId   || undefined,
    paramsTemplate:   w.paramsTemplate   || '{}',
    visualConfig:     w.visualConfig     || '{}',
    filterBindings:   w.filterBindings,
    interactions:     w.interactions     || '{}',
    filterKey:        w.filterKey    || undefined,
  };
}

/** Tính Y thấp nhất để thêm widget mới không chồng lấp */
function findNextY(widgets: DesignerWidget[]): number {
  if (widgets.length === 0) return 0;
  return Math.max(...widgets.map(w => w.gridY + w.gridH));
}

// ── Icons ──────────────────────────────────────────────────────────────────────

function ArrowLeftIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
    </svg>
  );
}

function SaveIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M8 7H5a2 2 0 00-2 2v9a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-3m-1 4l-3 3m0 0l-3-3m3 3V4" />
    </svg>
  );
}

function CheckIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
    </svg>
  );
}

function SpinnerIcon() {
  return (
    <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
    </svg>
  );
}

function TrashIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
    </svg>
  );
}

function SearchIcon() {
  return (
    <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0" />
    </svg>
  );
}

function PlusIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
    </svg>
  );
}

// ── DesignerCard ───────────────────────────────────────────────────────────────

interface DesignerCardProps {
  widget:    DesignerWidget;
  selected:  boolean;
  onSelect:  (key: string) => void;
  onDelete:  (key: string) => void;
}

function DesignerCard({ widget, selected, onSelect, onDelete }: DesignerCardProps) {
  const borderColor = selected ? 'border-[--brand]' : 'border-[--border]';

  function handleDelete(e: React.MouseEvent) {
    e.stopPropagation();
    onDelete(widget.widgetKey);
  }

  return (
    <div
      className={`flex flex-col h-full rounded-lg border-2 ${borderColor} bg-[--card] cursor-pointer transition-colors overflow-hidden`}
      onClick={() => onSelect(widget.widgetKey)}
    >
      {/* Header — drag handle */}
      <div
        className="drag-handle flex items-center justify-between px-2 py-1.5 bg-[--surface] border-b border-[--border] select-none cursor-grab active:cursor-grabbing"
      >
        <div className="flex items-center gap-1.5 min-w-0">
          {/* chartType badge */}
          <span className="flex-shrink-0 text-[10px] font-semibold px-1.5 py-0.5 rounded bg-[--brand-dim] text-[--brand] uppercase tracking-wide">
            {widget.chartType}
          </span>
          <span className="text-xs text-[--tx] truncate font-medium">
            {widget.title || widget.widgetKey}
          </span>
        </div>

        {/* Delete button */}
        <button
          className="flex-shrink-0 ml-1 p-0.5 rounded text-[--tx3] hover:text-[--danger] hover:bg-[--danger-bg] transition-colors"
          title="Xóa widget"
          onClick={handleDelete}
        >
          <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Body */}
      <div className="flex-1 flex items-center justify-center p-2 overflow-hidden">
        <div className="text-center">
          <div className="text-[--tx3] text-xs">
            {widget.subtitle || widget.operationPattern || <span className="italic">Chưa cấu hình</span>}
          </div>
          {selected && (
            <div className="mt-1 text-[10px] text-[--brand] font-medium">Đang chỉnh sửa ↗</div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── WidgetCatalogPanel ─────────────────────────────────────────────────────────

interface WidgetCatalogPanelProps {
  catalog:  WidgetTypeCatalogEntry[];
  onAdd:    (entry: WidgetTypeCatalogEntry) => void;
}

function WidgetCatalogPanel({ catalog, onAdd }: WidgetCatalogPanelProps) {
  const [search, setSearch] = useState('');

  const filtered = search.trim()
    ? catalog.filter(e => e.label.toLowerCase().includes(search.toLowerCase()))
    : catalog;

  // Nhóm theo category theo thứ tự đã định nghĩa
  const grouped = CATEGORY_ORDER.reduce<Record<string, WidgetTypeCatalogEntry[]>>((acc, cat) => {
    const entries = filtered.filter(e => e.category === cat);
    if (entries.length > 0) acc[cat] = entries;
    return acc;
  }, {});

  // Thêm các category không nằm trong CATEGORY_ORDER vào cuối
  filtered.forEach(e => {
    const cat = e.category;
    if (!CATEGORY_ORDER.includes(cat as WidgetCategory) && !grouped[cat]) {
      grouped[cat] = filtered.filter(x => x.category === cat);
    }
  });

  return (
    <div className="flex flex-col h-full">
      <div className="px-3 pt-3 pb-2">
        <h3 className="text-xs font-semibold text-[--tx2] uppercase tracking-wider mb-2">
          Widget Catalog
        </h3>

        {/* Search */}
        <div className="relative">
          <span className="absolute left-2 top-1/2 -translate-y-1/2 text-[--tx3]">
            <SearchIcon />
          </span>
          <input
            type="text"
            placeholder="Tìm kiếm..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full bg-[--bg] border border-[--border] rounded-md pl-7 pr-2 py-1.5 text-xs text-[--tx] placeholder-[--tx3] focus:outline-none focus:border-[--brand] transition-colors"
          />
        </div>
      </div>

      {/* Groups */}
      <div className="flex-1 overflow-y-auto px-3 pb-3 space-y-3">
        {Object.entries(grouped).map(([cat, entries]) => (
          <div key={cat}>
            <div className="text-[10px] font-semibold text-[--tx3] uppercase tracking-widest mb-1.5 pb-0.5 border-b border-[--border]">
              {CATEGORY_LABELS[cat as WidgetCategory] ?? cat}
            </div>
            <div className="flex flex-wrap gap-1.5">
              {entries.map(entry => (
                <button
                  key={entry.chartType}
                  onClick={() => onAdd(entry)}
                  title={entry.description ?? entry.label}
                  className="flex items-center gap-1 px-2 py-1 rounded-md text-xs bg-[--bg] border border-[--border] text-[--tx2] hover:border-[--brand] hover:text-[--brand] hover:bg-[--brand-dim] transition-colors"
                >
                  <span>{entry.icon ?? '📊'}</span>
                  <span>{entry.label}</span>
                </button>
              ))}
            </div>
          </div>
        ))}

        {Object.keys(grouped).length === 0 && (
          <div className="text-center py-6 text-[--tx3] text-xs">
            Không tìm thấy widget nào
          </div>
        )}
      </div>
    </div>
  );
}

// ── WidgetPropertiesPanel ──────────────────────────────────────────────────────

interface WidgetPropertiesPanelProps {
  widget:    DesignerWidget;
  onApply:   (updated: DesignerWidget) => void;
  onDelete:  (key: string) => void;
}

function WidgetPropertiesPanel({ widget, onApply, onDelete }: WidgetPropertiesPanelProps) {
  // Form state — khởi tạo từ widget hiện tại, reset khi widgetKey đổi
  const [form, setForm] = useState<DesignerWidget>(widget);

  useEffect(() => {
    setForm(widget);
  }, [widget.widgetKey]);

  function set<K extends keyof DesignerWidget>(key: K, value: DesignerWidget[K]) {
    setForm(prev => ({ ...prev, [key]: value }));
  }

  function handleApply() {
    onApply(form);
  }

  return (
    <div className="flex flex-col h-full">
      <div className="px-3 pt-3 pb-2 border-b border-[--border]">
        <h3 className="text-xs font-semibold text-[--tx2] uppercase tracking-wider">
          ✏ Thuộc tính Widget
        </h3>
        <div className="text-[10px] text-[--tx3] mt-0.5 font-mono truncate">{form.widgetKey}</div>
      </div>

      <div className="flex-1 overflow-y-auto px-3 py-3 space-y-3">
        {/* Title */}
        <FormField label="Tiêu đề">
          <input
            type="text"
            value={form.title}
            onChange={e => set('title', e.target.value)}
            placeholder="Tiêu đề widget"
            className={inputCls}
          />
        </FormField>

        {/* Subtitle */}
        <FormField label="Mô tả phụ">
          <input
            type="text"
            value={form.subtitle}
            onChange={e => set('subtitle', e.target.value)}
            placeholder="Mô tả ngắn"
            className={inputCls}
          />
        </FormField>

        {/* Chart Type */}
        <FormField label="Loại chart">
          <input
            type="text"
            value={form.chartType}
            onChange={e => set('chartType', e.target.value)}
            placeholder="vd: kpi_grid, line_chart..."
            className={inputCls}
          />
        </FormField>

        {/* Operation Pattern */}
        <FormField label="Operation Pattern">
          <input
            type="text"
            value={form.operationPattern}
            onChange={e => set('operationPattern', e.target.value)}
            placeholder="vd: report.dashboard.summary"
            className={inputCls}
          />
        </FormField>

        {/* Provider ID */}
        <FormField label="Provider ID">
          <input
            type="text"
            value={form.providerId}
            onChange={e => set('providerId', e.target.value)}
            placeholder="vd: excel-provider"
            className={inputCls}
          />
        </FormField>

        {/* Filter Key */}
        <FormField label="Filter Key">
          <input
            type="text"
            value={form.filterKey}
            onChange={e => set('filterKey', e.target.value)}
            placeholder="vd: date_range"
            className={inputCls}
          />
        </FormField>

        {/* Params Template */}
        <FormField label='Params Template (JSON)'>
          <textarea
            value={form.paramsTemplate}
            onChange={e => set('paramsTemplate', e.target.value)}
            rows={3}
            placeholder='{"date":"{{today}}"}'
            className={`${inputCls} resize-y font-mono text-[11px]`}
          />
        </FormField>

        {/* Visual Config */}
        <FormField label="Visual Config (JSON)">
          <textarea
            value={form.visualConfig}
            onChange={e => set('visualConfig', e.target.value)}
            rows={3}
            placeholder="{}"
            className={`${inputCls} resize-y font-mono text-[11px]`}
          />
        </FormField>

        {/* Grid info — read-only, cập nhật tự động qua drag/resize */}
        <FormField label="Vị trí lưới (tự động)">
          <div className="grid grid-cols-4 gap-1 text-[10px] text-[--tx3] font-mono">
            <span className="bg-[--bg] rounded px-1.5 py-1 text-center">x:{form.gridX}</span>
            <span className="bg-[--bg] rounded px-1.5 py-1 text-center">y:{form.gridY}</span>
            <span className="bg-[--bg] rounded px-1.5 py-1 text-center">w:{form.gridW}</span>
            <span className="bg-[--bg] rounded px-1.5 py-1 text-center">h:{form.gridH}</span>
          </div>
        </FormField>
      </div>

      {/* Actions */}
      <div className="px-3 py-3 border-t border-[--border] space-y-2">
        <button
          onClick={handleApply}
          className="w-full flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-md bg-[--brand] hover:bg-[--brand-hl] text-white text-xs font-medium transition-colors"
        >
          <CheckIcon />
          Áp dụng
        </button>
        <button
          onClick={() => onDelete(widget.widgetKey)}
          className="w-full flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-md bg-[--danger-bg] hover:bg-[rgba(255,82,82,0.2)] text-[--danger] border border-[--danger] border-opacity-30 text-xs font-medium transition-colors"
        >
          <TrashIcon />
          Xóa widget
        </button>
      </div>
    </div>
  );
}

// Reusable form field wrapper
const inputCls =
  'w-full bg-[--bg] border border-[--border] rounded-md px-2 py-1.5 text-xs text-[--tx] placeholder-[--tx3] focus:outline-none focus:border-[--brand] transition-colors';

function FormField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-[10px] font-medium text-[--tx3] uppercase tracking-wider mb-1">
        {label}
      </label>
      {children}
    </div>
  );
}

// ── Skeleton ───────────────────────────────────────────────────────────────────

function PageSkeleton() {
  return (
    <div className="flex flex-col h-full">
      {/* Header skeleton */}
      <div className="h-14 bg-[--surface] border-b border-[--border] flex items-center px-4 gap-3">
        <div className="h-8 w-24 rounded bg-[--overlay] animate-pulse" />
        <div className="h-5 w-48 rounded bg-[--overlay] animate-pulse" />
        <div className="ml-auto h-8 w-20 rounded bg-[--overlay] animate-pulse" />
      </div>
      {/* Body skeleton */}
      <div className="flex flex-1 overflow-hidden">
        <div className="flex-1 p-4 space-y-3">
          {[...Array(3)].map((_, i) => (
            <div key={i} className="h-32 rounded-lg bg-[--surface] animate-pulse" />
          ))}
        </div>
        <div className="w-72 bg-[--surface] border-l border-[--border]" />
      </div>
    </div>
  );
}

// ── DashboardDesigner ──────────────────────────────────────────────────────────

export function DashboardDesigner() {
  const { slug } = useParams<{ slug: string }>();
  const navigate  = useNavigate();

  // ── Data state ───────────────────────────────────────────────────────────────
  const [tabs,    setTabs]    = useState<ModuleTab[]>([]);
  const [catalog, setCatalog] = useState<WidgetTypeCatalogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Lưu toàn bộ widgets của mọi tab: Map<tabId, DesignerWidget[]>
  const [widgetsByTab, setWidgetsByTab] = useState<Map<string, DesignerWidget[]>>(new Map());

  // Tab đang active
  const [activeTabId, setActiveTabId] = useState<string>('');

  // Widget đang được chọn
  const [selectedKey, setSelectedKey] = useState<string | null>(null);

  // ── Save state ───────────────────────────────────────────────────────────────
  const [isDirty,      setIsDirty]      = useState(false);
  const [isSaving,     setIsSaving]     = useState(false);
  const [saveSuccess,  setSaveSuccess]  = useState(false);
  const [saveError,    setSaveError]    = useState<string | null>(null);

  // ── Canvas width ─────────────────────────────────────────────────────────────
  const canvasRef   = useRef<HTMLDivElement>(null);
  const [canvasWidth, setCanvasWidth] = useState(900);

  // ── Load dữ liệu ban đầu ─────────────────────────────────────────────────────

  const loadData = useCallback(async () => {
    if (!slug) return;
    setLoading(true);
    setLoadError(null);
    setSelectedKey(null);

    try {
      const [layout, schemas] = await Promise.all([
        apiGet<ModuleLayout>(`/api/v1/modules/${slug}/layout`),
        listWidgetSchemas(),
      ]);

      setCatalog(schemas);
      setTabs(layout.tabs);

      // Khởi tạo widgetsByTab
      const map = new Map<string, DesignerWidget[]>();
      layout.tabs.forEach(tab => {
        map.set(tab.id, tab.widgets.map(fromApiWidget));
      });
      setWidgetsByTab(map);

      // Chọn tab mặc định
      const defaultTab = layout.tabs.find(t => t.isDefault) ?? layout.tabs[0];
      if (defaultTab) setActiveTabId(defaultTab.id);

      setIsDirty(false);
    } catch (err) {
      setLoadError((err as Error).message ?? 'Tải dữ liệu thất bại');
    } finally {
      setLoading(false);
    }
  }, [slug]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  // ── Theo dõi kích thước canvas ───────────────────────────────────────────────

  useEffect(() => {
    const el = canvasRef.current;
    if (!el) return;

    const ro = new ResizeObserver(entries => {
      const w = entries[0]?.contentRect.width;
      if (w && w > 0) setCanvasWidth(w);
    });
    ro.observe(el);

    // Đo ngay lần đầu
    setCanvasWidth(el.clientWidth || 900);

    return () => ro.disconnect();
  }, [loading]); // Re-attach sau khi loading xong (DOM đã render)

  // ── Cảnh báo khi rời trang với thay đổi chưa lưu ────────────────────────────

  useEffect(() => {
    function handleBeforeUnload(e: BeforeUnloadEvent) {
      if (isDirty) {
        e.preventDefault();
        e.returnValue = '';
      }
    }
    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [isDirty]);

  // ── Widgets của tab hiện tại ─────────────────────────────────────────────────

  const widgets: DesignerWidget[] = widgetsByTab.get(activeTabId) ?? [];

  function setWidgets(updater: (prev: DesignerWidget[]) => DesignerWidget[]) {
    setWidgetsByTab(prev => {
      const next = new Map(prev);
      next.set(activeTabId, updater(next.get(activeTabId) ?? []));
      return next;
    });
    setIsDirty(true);
  }

  // ── Chuyển tab ───────────────────────────────────────────────────────────────

  function handleTabChange(tabId: string) {
    setActiveTabId(tabId);
    setSelectedKey(null);
  }

  // ── Grid layout change (kéo/resize) ─────────────────────────────────────────

  function handleLayoutChange(newLayout: Layout) {
    setWidgetsByTab(prev => {
      const current = prev.get(activeTabId) ?? [];
      const updated = current.map(w => {
        const li = newLayout.find((l: LayoutItem) => l.i === w.widgetKey);
        if (!li) return w;
        return { ...w, gridX: li.x, gridY: li.y, gridW: li.w, gridH: li.h };
      });
      const next = new Map(prev);
      next.set(activeTabId, updated);
      return next;
    });
    setIsDirty(true);
  }

  // ── Thêm widget từ catalog ───────────────────────────────────────────────────

  function handleAddWidget(entry: WidgetTypeCatalogEntry) {
    const sizes = DEFAULT_SIZES[entry.chartType] ?? { w: 6, h: 4 };
    const newWidget: DesignerWidget = {
      widgetKey:        generateKey(entry.chartType),
      title:            entry.label,
      subtitle:         '',
      chartType:        entry.chartType,
      gridX:            0,
      gridY:            findNextY(widgets),
      gridW:            sizes.w,
      gridH:            sizes.h,
      operationPattern: '',
      providerId:       '',
      paramsTemplate:   '{}',
      visualConfig:     '{}',
      filterBindings:   [],
      interactions:     '{}',
      filterKey:        '',
    };
    setWidgets(prev => [...prev, newWidget]);
    setSelectedKey(newWidget.widgetKey);
  }

  // ── Xóa widget ───────────────────────────────────────────────────────────────

  function handleDeleteWidget(key: string) {
    setWidgets(prev => prev.filter(w => w.widgetKey !== key));
    if (selectedKey === key) setSelectedKey(null);
  }

  // ── Áp dụng thay đổi thuộc tính ─────────────────────────────────────────────

  function handleApplyProperties(updated: DesignerWidget) {
    setWidgets(prev => prev.map(w => w.widgetKey === updated.widgetKey ? updated : w));
  }

  // ── Lưu ─────────────────────────────────────────────────────────────────────

  async function handleSave() {
    const tab = tabs.find(t => t.id === activeTabId);
    if (!tab) return;

    setIsSaving(true);
    setSaveError(null);

    try {
      const payload = widgets.map(toUpsertRequest);
      await saveWidgets(slug!, activeTabId, payload);
      setIsDirty(false);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 2000);
    } catch (err) {
      setSaveError((err as Error).message ?? 'Lưu thất bại');
    } finally {
      setIsSaving(false);
    }
  }

  // ── Grid layout array cho react-grid-layout ──────────────────────────────────

  const gridLayout: Layout = widgets.map(w => ({
    i: w.widgetKey,
    x: w.gridX,
    y: w.gridY,
    w: w.gridW,
    h: w.gridH,
    minW: 2,
    minH: 1,
  }));

  // ── Render ────────────────────────────────────────────────────────────────────

  if (loading) return <PageSkeleton />;

  if (loadError) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-4 text-[--tx2]">
        <div className="text-[--danger] text-sm">{loadError}</div>
        <button
          onClick={() => void loadData()}
          className="px-4 py-2 rounded-md bg-[--brand] hover:bg-[--brand-hl] text-white text-sm transition-colors"
        >
          Thử lại
        </button>
      </div>
    );
  }

  const selectedWidget = selectedKey ? widgets.find(w => w.widgetKey === selectedKey) ?? null : null;
  const activeTab = tabs.find(t => t.id === activeTabId);

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* ── Header ───────────────────────────────────────────────────────────── */}
      <header className="h-14 flex-shrink-0 flex items-center gap-3 px-4 bg-[--surface] border-b border-[--border]">
        {/* Quay lại */}
        <button
          onClick={() => navigate('/admin/menus')}
          className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs text-[--tx2] hover:text-[--tx] hover:bg-[--overlay] transition-colors"
        >
          <ArrowLeftIcon />
          Quay lại
        </button>

        <div className="w-px h-5 bg-[--border]" />

        {/* Module title */}
        <h1 className="text-sm font-semibold text-[--tx] truncate">
          {slug ?? 'Module'} — Dashboard Designer
        </h1>

        {/* Tab selector */}
        {tabs.length > 0 && (
          <div className="flex items-center gap-1 ml-2">
            <span className="text-xs text-[--tx3]">Tab:</span>
            <div className="flex gap-1">
              {tabs.map(tab => (
                <button
                  key={tab.id}
                  onClick={() => handleTabChange(tab.id)}
                  className={[
                    'px-2.5 py-1 rounded-md text-xs font-medium transition-colors',
                    tab.id === activeTabId
                      ? 'bg-[--brand] text-white'
                      : 'text-[--tx2] hover:bg-[--overlay] hover:text-[--tx]',
                  ].join(' ')}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Spacer */}
        <div className="flex-1" />

        {/* Save error */}
        {saveError && (
          <span className="text-xs text-[--danger] max-w-xs truncate">{saveError}</span>
        )}

        {/* Dirty indicator */}
        {isDirty && !isSaving && !saveSuccess && (
          <span className="text-xs text-[--warning]">Chưa lưu</span>
        )}

        {/* Save button */}
        <button
          onClick={() => void handleSave()}
          disabled={!isDirty || isSaving}
          className={[
            'flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium transition-colors',
            saveSuccess
              ? 'bg-[--success] text-white'
              : isDirty && !isSaving
                ? 'bg-[--brand] hover:bg-[--brand-hl] text-white'
                : 'bg-[--overlay] text-[--tx3] cursor-not-allowed',
          ].join(' ')}
        >
          {isSaving    ? <SpinnerIcon /> : saveSuccess ? <CheckIcon /> : <SaveIcon />}
          {isSaving ? 'Đang lưu...' : saveSuccess ? 'Đã lưu!' : 'Lưu'}
        </button>
      </header>

      {/* ── Body ─────────────────────────────────────────────────────────────── */}
      <div className="flex flex-1 overflow-hidden">
        {/* Canvas */}
        <div className="flex-1 overflow-auto bg-[--bg] relative" ref={canvasRef}>
          {/* Canvas content */}
          <div className="min-h-full">
            {widgets.length === 0 ? (
              // Empty state
              <div className="flex flex-col items-center justify-center h-[400px] gap-3 text-[--tx3]">
                <div className="text-4xl">🎨</div>
                <div className="text-sm">Canvas trống</div>
                <div className="text-xs">Chọn widget từ bảng bên phải để thêm vào canvas</div>
              </div>
            ) : (
              <GridLayout
                className="layout"
                layout={gridLayout}
                width={canvasWidth}
                onLayoutChange={handleLayoutChange}
                gridConfig={{ cols: 12, rowHeight: 60, margin: [8, 8], containerPadding: [12, 12] }}
                dragConfig={{ enabled: true, handle: '.drag-handle' }}
                resizeConfig={{ enabled: true }}
              >
                {widgets.map(w => (
                  <div key={w.widgetKey}>
                    <DesignerCard
                      widget={w}
                      selected={selectedKey === w.widgetKey}
                      onSelect={setSelectedKey}
                      onDelete={handleDeleteWidget}
                    />
                  </div>
                ))}
              </GridLayout>
            )}
          </div>

          {/* Thêm widget button — floating bottom-left */}
          <div className="sticky bottom-4 left-4 mt-4 pl-4">
            <button
              onClick={() => setSelectedKey(null)} // Clear selection to show catalog
              className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[--surface] border border-[--border] text-xs text-[--tx2] hover:border-[--brand] hover:text-[--brand] hover:bg-[--brand-dim] transition-colors shadow-lg"
            >
              <PlusIcon />
              Thêm widget
            </button>
          </div>
        </div>

        {/* ── Right Panel ──────────────────────────────────────────────────── */}
        <aside className="w-72 flex-shrink-0 bg-[--surface] border-l border-[--border] flex flex-col overflow-hidden">
          {selectedWidget ? (
            <WidgetPropertiesPanel
              key={selectedWidget.widgetKey}
              widget={selectedWidget}
              onApply={handleApplyProperties}
              onDelete={handleDeleteWidget}
            />
          ) : (
            <WidgetCatalogPanel
              catalog={catalog}
              onAdd={handleAddWidget}
            />
          )}

          {/* Tab info footer */}
          {activeTab && (
            <div className="flex-shrink-0 px-3 py-2 border-t border-[--border] bg-[--bg] bg-opacity-30">
              <div className="text-[10px] text-[--tx3]">
                Tab: <span className="text-[--tx2] font-medium">{activeTab.label}</span>
                {' · '}
                <span>{widgets.length} widget{widgets.length !== 1 ? 's' : ''}</span>
              </div>
            </div>
          )}
        </aside>
      </div>
    </div>
  );
}
