import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiGet } from '../api/client';
import type { MenuSummary } from '../types/menuTypes';

interface MenuTree {
  root: MenuSummary;
  children: MenuSummary[];
}

function buildTree(menus: MenuSummary[]): MenuTree[] {
  const roots = menus.filter((m) => m.parentId === null).sort((a, b) => a.sortOrder - b.sortOrder);
  return roots.map((root) => ({
    root,
    children: menus
      .filter((m) => m.parentId === root.id)
      .sort((a, b) => a.sortOrder - b.sortOrder),
  }));
}

function SkeletonCard() {
  return (
    <div className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm animate-pulse">
      <div className="mb-3 h-8 w-8 rounded bg-gray-200" />
      <div className="mb-2 h-4 w-2/3 rounded bg-gray-200" />
      <div className="h-3 w-full rounded bg-gray-100" />
    </div>
  );
}

export function ReportViewer() {
  const navigate = useNavigate();
  const [menus, setMenus] = useState<MenuSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    apiGet<MenuSummary[]>('/api/v1/reports/menus')
      .then((data) => setMenus(data))
      .catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : 'Không thể tải danh sách menu';
        setError(msg);
      })
      .finally(() => setLoading(false));
  }, []);

  const tree = buildTree(menus);

  return (
    <div className="p-6">
      {/* Page header */}
      <div className="mb-6">
        <h1 className="text-xl font-bold text-gray-800">Báo cáo</h1>
        <p className="text-sm text-gray-500">Chọn menu báo cáo để xem</p>
      </div>

      {/* Loading */}
      {loading && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <SkeletonCard key={i} />
          ))}
        </div>
      )}

      {/* Error */}
      {!loading && error && (
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-red-700">
          <p className="font-semibold">Lỗi tải dữ liệu</p>
          <p className="mt-1 text-sm">{error}</p>
        </div>
      )}

      {/* Empty */}
      {!loading && !error && menus.length === 0 && (
        <div className="rounded-xl border border-gray-200 bg-white p-10 text-center shadow-sm">
          <p className="text-gray-500">Bạn chưa được phân quyền truy cập menu báo cáo nào.</p>
        </div>
      )}

      {/* Menu tree */}
      {!loading && !error && tree.length > 0 && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {tree.map(({ root, children }) => (
            <div key={root.id} className="flex flex-col gap-2">
              {/* Root card */}
              <button
                type="button"
                onClick={() => navigate('/reports/' + root.slug)}
                className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm text-left hover:border-brand-300 hover:shadow-md transition-all focus:outline-none focus:ring-2 focus:ring-brand-400"
              >
                <div className="mb-3 text-2xl leading-none">{root.icon}</div>
                <div className="text-base font-bold text-gray-800">{root.name}</div>
                {root.description && (
                  <div className="mt-1 text-[0.85rem] text-gray-500">{root.description}</div>
                )}
              </button>

              {/* Child sub-cards */}
              {children.length > 0 && (
                <div className="ml-4 flex flex-col gap-2">
                  {children.map((child) => (
                    <button
                      key={child.id}
                      type="button"
                      onClick={() => navigate('/reports/' + child.slug)}
                      className="rounded-lg border border-gray-200 bg-white px-4 py-3 shadow-sm text-left hover:border-brand-300 hover:shadow-md transition-all focus:outline-none focus:ring-2 focus:ring-brand-400"
                    >
                      <span className="mr-2 text-base">{child.icon}</span>
                      <span className="text-sm font-semibold text-gray-700">{child.name}</span>
                      {child.description && (
                        <p className="mt-0.5 text-[0.82rem] text-gray-400">{child.description}</p>
                      )}
                    </button>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
