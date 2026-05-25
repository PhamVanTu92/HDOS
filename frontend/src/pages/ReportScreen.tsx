import { useEffect, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { apiGet } from '../api/client';
import type { MenuDetail, ScreenDetail, WidgetDef, WidgetConfig } from '../types/menuTypes';

// ---------------------------------------------------------------------------
// Data-source badge
// ---------------------------------------------------------------------------
function DataSourceBadge({ source }: { source: string }) {
  let cls = 'bg-gray-100 text-gray-600';
  if (source.startsWith('sql.')) cls = 'bg-blue-100 text-blue-700';
  else if (source.startsWith('excel.')) cls = 'bg-green-100 text-green-700';
  else if (source.startsWith('ml.')) cls = 'bg-purple-100 text-purple-700';

  return (
    <span className={`rounded px-1.5 py-0.5 text-[0.72rem] font-medium ${cls}`}>{source}</span>
  );
}

// ---------------------------------------------------------------------------
// Widget card
// ---------------------------------------------------------------------------
function WidgetCard({ widget }: { widget: WidgetDef }) {
  const [toastVisible, setToastVisible] = useState(false);

  let config: WidgetConfig = {};
  try {
    config = JSON.parse(widget.config) as WidgetConfig;
  } catch {
    // ignore invalid json
  }

  function handleRun() {
    setToastVisible(true);
    setTimeout(() => setToastVisible(false), 3000);
  }

  function renderBody() {
    switch (widget.widgetType) {
      case 'kpi':
        return (
          <div className="flex flex-col items-start gap-1 py-2">
            <span className="text-3xl font-black text-brand-600">—</span>
            <span className="text-sm text-gray-400">Chưa có dữ liệu</span>
          </div>
        );
      case 'line':
        return (
          <div className="flex h-40 items-center justify-center rounded-lg bg-gray-50 text-3xl text-gray-300">
            📈
          </div>
        );
      case 'bar':
        return (
          <div className="flex h-40 items-center justify-center rounded-lg bg-gray-50 text-3xl text-gray-300">
            📊
          </div>
        );
      case 'pie':
        return (
          <div className="flex h-40 items-center justify-center rounded-lg bg-gray-50 text-3xl text-gray-300">
            🥧
          </div>
        );
      case 'table':
        return <div className="h-32 rounded bg-gray-50" />;
      case 'text':
        return (
          <p className="italic text-sm text-gray-400">Nội dung văn bản sẽ hiển thị ở đây.</p>
        );
      default:
        return null;
    }
  }

  return (
    <div
      className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm flex flex-col gap-3 relative"
      style={{ gridColumn: `span ${widget.colSpan}` }}
    >
      {/* Header */}
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm font-bold text-gray-700 leading-tight">{widget.title}</span>
        <div className="flex items-center gap-1.5 shrink-0">
          {widget.dataSource && <DataSourceBadge source={widget.dataSource} />}
          {config.operation && (
            <button
              type="button"
              onClick={handleRun}
              className="rounded px-2 py-0.5 text-[0.72rem] font-medium bg-brand-600 text-white hover:bg-brand-700 transition-colors"
            >
              Chạy dữ liệu
            </button>
          )}
        </div>
      </div>

      {/* Body */}
      {renderBody()}

      {/* Toast */}
      {toastVisible && (
        <div className="absolute bottom-3 left-1/2 -translate-x-1/2 rounded-lg bg-gray-800 px-3 py-1.5 text-xs text-white shadow-lg whitespace-nowrap">
          Backend sẽ thực thi operation khi deploy
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Widget grid skeleton
// ---------------------------------------------------------------------------
function WidgetGridSkeleton() {
  return (
    <div
      className="grid gap-4"
      style={{ gridTemplateColumns: 'repeat(12, minmax(0, 1fr))' }}
    >
      {Array.from({ length: 4 }).map((_, i) => (
        <div
          key={i}
          className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm animate-pulse"
          style={{ gridColumn: 'span 6' }}
        >
          <div className="mb-3 h-4 w-1/2 rounded bg-gray-200" />
          <div className="h-32 rounded bg-gray-100" />
        </div>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------
// ReportScreen page
// ---------------------------------------------------------------------------
export function ReportScreen() {
  const navigate = useNavigate();
  const { menuSlug } = useParams<{ menuSlug: string }>();
  const [searchParams, setSearchParams] = useSearchParams();

  const [menu, setMenu] = useState<MenuDetail | null>(null);
  const [menuLoading, setMenuLoading] = useState(true);
  const [menuError, setMenuError] = useState<string | null>(null);

  const [screen, setScreen] = useState<ScreenDetail | null>(null);
  const [screenLoading, setScreenLoading] = useState(false);
  const [screenError, setScreenError] = useState<string | null>(null);

  const selectedScreenId = searchParams.get('screenId') ?? '';

  // Load menu detail
  useEffect(() => {
    if (!menuSlug) return;
    setMenuLoading(true);
    setMenuError(null);
    apiGet<MenuDetail>('/api/v1/reports/menus/' + menuSlug)
      .then((data) => {
        setMenu(data);
        // Auto-select first screen if no screenId in query
        if (!searchParams.get('screenId') && data.screens.length > 0) {
          setSearchParams({ screenId: data.screens[0].id }, { replace: true });
        }
      })
      .catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : 'Không thể tải menu';
        setMenuError(msg);
      })
      .finally(() => setMenuLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [menuSlug]);

  // Load screen detail
  useEffect(() => {
    if (!menuSlug || !selectedScreenId) return;
    setScreenLoading(true);
    setScreenError(null);
    setScreen(null);
    apiGet<ScreenDetail>(
      '/api/v1/reports/menus/' + menuSlug + '/screens/' + selectedScreenId,
    )
      .then((data) => setScreen(data))
      .catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : 'Không thể tải màn hình';
        setScreenError(msg);
      })
      .finally(() => setScreenLoading(false));
  }, [menuSlug, selectedScreenId]);

  function selectScreen(id: string) {
    setSearchParams({ screenId: id });
  }

  // ---- render ----

  if (menuLoading) {
    return (
      <div className="flex h-full items-center justify-center p-12">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-brand-600 border-t-transparent" />
      </div>
    );
  }

  if (menuError) {
    return (
      <div className="p-6">
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-red-700">
          <p className="font-semibold">Lỗi tải menu</p>
          <p className="mt-1 text-sm">{menuError}</p>
        </div>
      </div>
    );
  }

  if (!menu) return null;

  const sortedWidgets = screen
    ? [...screen.widgets].sort((a, b) => a.sortOrder - b.sortOrder)
    : [];

  return (
    <div className="flex h-full overflow-hidden">
      {/* ---- Sidebar ---- */}
      <aside className="flex w-56 shrink-0 flex-col border-r border-gray-200 bg-gray-50">
        {/* Sidebar header */}
        <div className="flex items-center gap-2 border-b border-gray-200 px-4 py-4">
          <span className="text-xl">{menu.icon}</span>
          <span className="text-sm font-bold text-gray-800 leading-tight">{menu.name}</span>
        </div>

        {/* Back link */}
        <button
          type="button"
          onClick={() => navigate('/reports')}
          className="flex items-center gap-1.5 px-4 py-2 text-xs text-gray-400 hover:text-brand-600 transition-colors"
        >
          ← Tất cả báo cáo
        </button>

        {/* Screen list */}
        <nav className="flex-1 overflow-y-auto py-2">
          {menu.screens.length === 0 && (
            <p className="px-4 py-3 text-xs text-gray-400">Không có màn hình nào</p>
          )}
          {menu.screens
            .slice()
            .sort((a, b) => a.sortOrder - b.sortOrder)
            .map((s) => {
              const active = s.id === selectedScreenId;
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => selectScreen(s.id)}
                  className={[
                    'flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors text-left',
                    active
                      ? 'border-l-2 border-brand-600 bg-brand-50 text-brand-700 font-semibold'
                      : 'border-l-2 border-transparent text-gray-600 hover:bg-gray-100',
                  ].join(' ')}
                >
                  <span className="text-base leading-none">{s.icon}</span>
                  <span className="leading-tight">{s.name}</span>
                </button>
              );
            })}
        </nav>
      </aside>

      {/* ---- Main area ---- */}
      <main className="flex flex-1 flex-col overflow-auto">
        {/* Screen header */}
        <div className="border-b border-gray-200 bg-white px-6 py-4">
          {screen ? (
            <div className="flex items-center gap-3">
              <span className="text-xl">{screen.icon}</span>
              <h2 className="text-xl font-bold text-gray-800">{screen.name}</h2>
            </div>
          ) : (
            <div className="h-7 w-48 animate-pulse rounded bg-gray-200" />
          )}
        </div>

        {/* Widget area */}
        <div className="flex-1 p-6">
          {screenLoading && <WidgetGridSkeleton />}

          {!screenLoading && screenError && (
            <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-red-700">
              <p className="font-semibold">Lỗi tải màn hình</p>
              <p className="mt-1 text-sm">{screenError}</p>
            </div>
          )}

          {!screenLoading && !screenError && screen && sortedWidgets.length === 0 && (
            <div className="rounded-xl border border-gray-200 bg-white p-10 text-center shadow-sm">
              <p className="text-gray-400">Màn hình này chưa có widget nào.</p>
            </div>
          )}

          {!screenLoading && !screenError && screen && sortedWidgets.length > 0 && (
            <div
              className="grid gap-4"
              style={{ gridTemplateColumns: 'repeat(12, minmax(0, 1fr))' }}
            >
              {sortedWidgets.map((w) => (
                <WidgetCard key={w.id} widget={w} />
              ))}
            </div>
          )}

          {!screenLoading && !screen && !screenError && !selectedScreenId && (
            <div className="rounded-xl border border-gray-200 bg-white p-10 text-center shadow-sm">
              <p className="text-gray-400">Chọn một màn hình từ danh sách bên trái.</p>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
