/**
 * MenuManager — Mockup tương tác (chưa có backend, dùng state local)
 * Route: /admin/menus
 *
 * Chức năng demo:
 *  - Cây menu bên trái (chọn, expand/collapse)
 *  - Tab Thông tin: sửa tên, slug, icon, vị trí, mô tả, hiển thị
 *  - Tab Báo cáo: danh sách kèm drag handle (visual), bật/tắt từng item, thêm báo cáo
 *  - Tab Phân quyền: gán role/user, toggle Xem/Xuất, xóa
 *  - Tạo menu mới / xóa menu
 */

import { useState } from 'react';

// ── Types ──────────────────────────────────────────────────────────────────────

interface ReportItem {
  id: string;
  name: string;
  type: 'chart' | 'table' | 'mixed';
  visible: boolean;
}

interface Permission {
  id: string;
  kind: 'role' | 'user';
  value: string;
  label: string;
  canView: boolean;
  canExport: boolean;
}

interface MenuNode {
  id: string;
  name: string;
  slug: string;
  icon: string;
  description: string;
  position: number;
  visible: boolean;
  parentId: string | null;
  reports: ReportItem[];
  permissions: Permission[];
  children: string[]; // child ids
}

type MenuMap = Record<string, MenuNode>;

// ── Mock data ──────────────────────────────────────────────────────────────────

const INIT_MENUS: MenuMap = {
  '1': {
    id: '1', name: 'Kinh doanh', slug: 'kinh-doanh', icon: '📊',
    description: 'Doanh thu, bán hàng, KPI kinh doanh',
    position: 1, visible: true, parentId: null,
    reports: [
      { id: 'r1', name: 'Doanh thu theo tháng',    type: 'chart', visible: true  },
      { id: 'r2', name: 'So sánh doanh thu năm',   type: 'table', visible: true  },
      { id: 'r3', name: 'Top 10 sản phẩm bán chạy',type: 'chart', visible: false },
    ],
    permissions: [
      { id: 'p1', kind: 'role', value: 'admin',   label: 'admin',                    canView: true,  canExport: true  },
      { id: 'p2', kind: 'role', value: 'manager', label: 'manager',                  canView: true,  canExport: true  },
      { id: 'p3', kind: 'user', value: 'u1',      label: 'nguyen.van.a@hdos.local',  canView: true,  canExport: false },
    ],
    children: [],
  },
  '2': {
    id: '2', name: 'Vận hành', slug: 'van-hanh', icon: '⚙️',
    description: 'Hiệu suất vận hành, tiến độ sản xuất',
    position: 2, visible: true, parentId: null,
    reports: [
      { id: 'r4', name: 'Hiệu suất sản xuất', type: 'table', visible: true },
      { id: 'r5', name: 'Báo cáo tồn kho',    type: 'chart', visible: true },
    ],
    permissions: [
      { id: 'p4', kind: 'role', value: 'admin',    label: 'admin',    canView: true, canExport: true  },
      { id: 'p5', kind: 'role', value: 'operator', label: 'operator', canView: true, canExport: false },
    ],
    children: ['2-1'],
  },
  '2-1': {
    id: '2-1', name: 'Chất lượng', slug: 'van-hanh/chat-luong', icon: '✅',
    description: 'Kiểm tra chất lượng sản phẩm',
    position: 1, visible: true, parentId: '2',
    reports: [
      { id: 'r6', name: 'Tỷ lệ lỗi sản phẩm', type: 'chart', visible: true },
    ],
    permissions: [
      { id: 'p6', kind: 'role', value: 'admin', label: 'admin', canView: true, canExport: true },
    ],
    children: [],
  },
  '3': {
    id: '3', name: 'Quản trị', slug: 'quan-tri', icon: '🛡️',
    description: 'Báo cáo dành riêng quản trị viên',
    position: 3, visible: false, parentId: null,
    reports: [],
    permissions: [
      { id: 'p7', kind: 'role', value: 'admin', label: 'admin', canView: true, canExport: true },
    ],
    children: [],
  },
};

const ROOT_IDS = ['1', '2', '3'];

const ICON_OPTIONS = ['📊','📈','📉','📋','📌','🗃️','⚙️','✅','🛡️','🏭','💼','🎯','🔍','📁','🌐','⭐'];
const ROLE_OPTIONS  = ['admin', 'manager', 'operator', 'viewer', 'user'];
const REPORT_OPTIONS = [
  { id: 'ar1', name: 'Báo cáo tổng hợp Q1' },
  { id: 'ar2', name: 'Phân tích khách hàng' },
  { id: 'ar3', name: 'Báo cáo nhân sự' },
  { id: 'ar4', name: 'Dashboard tổng quan' },
  { id: 'ar5', name: 'Báo cáo tài chính' },
];

// ── Helpers ────────────────────────────────────────────────────────────────────

function uid() { return Math.random().toString(36).slice(2, 8); }

function slugify(s: string) {
  return s.toLowerCase()
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/đ/g, 'd').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

function typeBadge(t: ReportItem['type']) {
  const m: Record<string, string> = { chart: 'Biểu đồ', table: 'Bảng', mixed: 'Kết hợp' };
  const c: Record<string, string> = { chart: 'bg-blue-100 text-blue-700', table: 'bg-purple-100 text-purple-700', mixed: 'bg-orange-100 text-orange-700' };
  return <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${c[t]}`}>{m[t]}</span>;
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function TreeNode({
  id, menus, selectedId, expanded, onSelect, onToggleExpand,
}: {
  id: string; menus: MenuMap; selectedId: string | null;
  expanded: Set<string>;
  onSelect: (id: string) => void;
  onToggleExpand: (id: string) => void;
}) {
  const node = menus[id];
  if (!node) return null;
  const hasChildren = node.children.length > 0;
  const isOpen = expanded.has(id);

  return (
    <div>
      <button
        onClick={() => onSelect(id)}
        className={`flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors ${
          selectedId === id
            ? 'bg-brand-600 text-white'
            : 'text-gray-700 hover:bg-gray-100'
        }`}
      >
        {hasChildren ? (
          <span
            className="shrink-0 text-gray-400 hover:text-gray-700"
            onClick={e => { e.stopPropagation(); onToggleExpand(id); }}
          >
            {isOpen ? '▾' : '▸'}
          </span>
        ) : <span className="w-3 shrink-0" />}
        <span className="text-base leading-none">{node.icon}</span>
        <span className="flex-1 truncate font-medium">{node.name}</span>
        {!node.visible && (
          <span className="shrink-0 rounded bg-gray-200 px-1 py-0.5 text-[10px] text-gray-500">Ẩn</span>
        )}
      </button>

      {hasChildren && isOpen && (
        <div className="ml-5 mt-0.5 border-l border-gray-200 pl-2 space-y-0.5">
          {node.children.map(cid => (
            <TreeNode key={cid} id={cid} menus={menus} selectedId={selectedId}
              expanded={expanded} onSelect={onSelect} onToggleExpand={onToggleExpand} />
          ))}
        </div>
      )}
    </div>
  );
}

// ── Tab: Thông tin ─────────────────────────────────────────────────────────────

function TabInfo({ node, onChange }: { node: MenuNode; onChange: (patch: Partial<MenuNode>) => void }) {
  const [showIcons, setShowIcons] = useState(false);

  return (
    <div className="space-y-5">
      {/* Icon + Tên */}
      <div className="flex gap-3">
        <div className="relative">
          <button
            onClick={() => setShowIcons(!showIcons)}
            className="flex h-10 w-10 items-center justify-center rounded-lg border border-gray-300 text-xl hover:border-brand-500 hover:bg-brand-50"
            title="Chọn icon"
          >
            {node.icon}
          </button>
          {showIcons && (
            <div className="absolute left-0 top-12 z-10 grid grid-cols-4 gap-1 rounded-xl border border-gray-200 bg-white p-2 shadow-lg">
              {ICON_OPTIONS.map(ic => (
                <button key={ic} onClick={() => { onChange({ icon: ic }); setShowIcons(false); }}
                  className={`rounded p-1.5 text-xl hover:bg-gray-100 ${node.icon === ic ? 'bg-brand-100 ring-1 ring-brand-400' : ''}`}>
                  {ic}
                </button>
              ))}
            </div>
          )}
        </div>
        <div className="flex-1">
          <label className="mb-1 block text-xs font-medium text-gray-600">Tên menu *</label>
          <input
            value={node.name}
            onChange={e => onChange({ name: e.target.value, slug: slugify(e.target.value) })}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            placeholder="Ví dụ: Báo cáo Kinh doanh"
          />
        </div>
      </div>

      {/* Slug */}
      <div>
        <label className="mb-1 block text-xs font-medium text-gray-600">Đường dẫn (slug)</label>
        <div className="flex items-center rounded-lg border border-gray-300 focus-within:border-brand-500 focus-within:ring-1 focus-within:ring-brand-500">
          <span className="border-r border-gray-200 bg-gray-50 px-3 py-2 text-xs text-gray-400 rounded-l-lg">/reports/</span>
          <input
            value={node.slug}
            onChange={e => onChange({ slug: e.target.value })}
            className="flex-1 px-3 py-2 text-sm focus:outline-none rounded-r-lg"
          />
        </div>
      </div>

      {/* Mô tả */}
      <div>
        <label className="mb-1 block text-xs font-medium text-gray-600">Mô tả</label>
        <textarea
          value={node.description}
          onChange={e => onChange({ description: e.target.value })}
          rows={2}
          className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm resize-none focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          placeholder="Mô tả ngắn về nhóm báo cáo này..."
        />
      </div>

      {/* Vị trí + Hiển thị */}
      <div className="flex items-center gap-6">
        <div>
          <label className="mb-1 block text-xs font-medium text-gray-600">Thứ tự hiển thị</label>
          <input
            type="number" min={1}
            value={node.position}
            onChange={e => onChange({ position: Number(e.target.value) })}
            className="w-20 rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          />
        </div>
        <div>
          <label className="mb-1 block text-xs font-medium text-gray-600">Trạng thái</label>
          <button
            onClick={() => onChange({ visible: !node.visible })}
            className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition-colors ${
              node.visible
                ? 'border-green-300 bg-green-50 text-green-700'
                : 'border-gray-300 bg-gray-50 text-gray-500'
            }`}
          >
            <span className={`h-2 w-2 rounded-full ${node.visible ? 'bg-green-500' : 'bg-gray-400'}`} />
            {node.visible ? 'Hiển thị' : 'Ẩn'}
          </button>
        </div>
      </div>

      <div className="rounded-xl bg-blue-50 border border-blue-100 p-3 text-xs text-blue-700">
        💡 Menu con sẽ xuất hiện dưới dạng submenu khi người dùng hover. Kéo menu trong cây để thay đổi cấp cha/con (chức năng drag sẽ có sau khi confirm thiết kế).
      </div>
    </div>
  );
}

// ── Tab: Báo cáo ───────────────────────────────────────────────────────────────

function TabReports({ node, onChange }: { node: MenuNode; onChange: (patch: Partial<MenuNode>) => void }) {
  const [showAdd, setShowAdd] = useState(false);

  const toggle = (id: string) =>
    onChange({ reports: node.reports.map(r => r.id === id ? { ...r, visible: !r.visible } : r) });

  const remove = (id: string) =>
    onChange({ reports: node.reports.filter(r => r.id !== id) });

  const addReport = (opt: { id: string; name: string }) => {
    if (node.reports.some(r => r.id === opt.id)) return;
    onChange({ reports: [...node.reports, { id: opt.id, name: opt.name, type: 'table', visible: true }] });
    setShowAdd(false);
  };

  return (
    <div className="space-y-3">
      <p className="text-xs text-gray-500">
        Kéo ⠿ để sắp xếp thứ tự hiển thị. Bật/tắt từng báo cáo mà không cần xóa khỏi menu.
      </p>

      {node.reports.length === 0 && (
        <div className="rounded-xl border-2 border-dashed border-gray-200 py-10 text-center text-sm text-gray-400">
          Chưa có báo cáo nào. Nhấn <strong>+ Thêm báo cáo</strong> để bắt đầu.
        </div>
      )}

      <div className="space-y-2">
        {node.reports.map((r, i) => (
          <div key={r.id}
            className={`flex items-center gap-3 rounded-xl border px-4 py-3 transition-colors ${
              r.visible ? 'border-gray-200 bg-white' : 'border-gray-100 bg-gray-50 opacity-60'
            }`}
          >
            {/* Drag handle */}
            <span className="cursor-grab text-gray-300 select-none text-lg" title="Kéo để sắp xếp">⠿</span>
            <span className="w-5 text-xs font-mono text-gray-400">{i + 1}</span>
            <div className="flex-1 min-w-0">
              <p className={`truncate text-sm font-medium ${r.visible ? 'text-gray-800' : 'text-gray-400'}`}>{r.name}</p>
              <div className="mt-0.5">{typeBadge(r.type)}</div>
            </div>
            {/* Toggle visible */}
            <button
              onClick={() => toggle(r.id)}
              title={r.visible ? 'Ẩn báo cáo này' : 'Hiện báo cáo này'}
              className={`rounded-lg px-2 py-1 text-xs font-medium transition-colors ${
                r.visible
                  ? 'bg-green-100 text-green-700 hover:bg-green-200'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200'
              }`}
            >
              {r.visible ? 'Hiện' : 'Ẩn'}
            </button>
            <button onClick={() => remove(r.id)}
              className="rounded-lg p-1.5 text-gray-300 hover:bg-red-50 hover:text-red-500 transition-colors">
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        ))}
      </div>

      {/* Add report */}
      {showAdd ? (
        <div className="rounded-xl border border-brand-200 bg-brand-50 p-3 space-y-2">
          <p className="text-xs font-medium text-brand-700">Chọn báo cáo để thêm vào menu:</p>
          <div className="space-y-1">
            {REPORT_OPTIONS.filter(o => !node.reports.some(r => r.id === o.id)).map(opt => (
              <button key={opt.id}
                onClick={() => addReport(opt)}
                className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm text-left hover:bg-white hover:shadow-sm transition-all"
              >
                <span className="text-brand-500">+</span>
                {opt.name}
              </button>
            ))}
          </div>
          <button onClick={() => setShowAdd(false)} className="text-xs text-gray-400 hover:text-gray-600">Hủy</button>
        </div>
      ) : (
        <button
          onClick={() => setShowAdd(true)}
          className="flex w-full items-center justify-center gap-2 rounded-xl border-2 border-dashed border-gray-200 py-3 text-sm text-gray-500 hover:border-brand-300 hover:text-brand-600 transition-colors"
        >
          <span className="text-lg">+</span> Thêm báo cáo vào menu này
        </button>
      )}
    </div>
  );
}

// ── Tab: Phân quyền ────────────────────────────────────────────────────────────

function TabPermissions({ node, onChange }: { node: MenuNode; onChange: (patch: Partial<MenuNode>) => void }) {
  const [showAdd, setShowAdd] = useState(false);
  const [addKind, setAddKind] = useState<'role' | 'user'>('role');
  const [addValue, setAddValue] = useState('');

  const togglePerm = (id: string, field: 'canView' | 'canExport') =>
    onChange({ permissions: node.permissions.map(p => p.id === id ? { ...p, [field]: !p[field as keyof Permission] } : p) });

  const remove = (id: string) =>
    onChange({ permissions: node.permissions.filter(p => p.id !== id) });

  const add = () => {
    if (!addValue) return;
    const exists = node.permissions.some(p => p.kind === addKind && p.value === addValue);
    if (exists) { setShowAdd(false); return; }
    onChange({
      permissions: [...node.permissions, {
        id: uid(), kind: addKind, value: addValue,
        label: addKind === 'role' ? addValue : `${addValue}@hdos.local`,
        canView: true, canExport: false,
      }],
    });
    setAddValue('');
    setShowAdd(false);
  };

  return (
    <div className="space-y-4">
      <div className="rounded-xl bg-amber-50 border border-amber-100 p-3 text-xs text-amber-700">
        ⚠️ Người dùng chỉ thấy menu nếu được cấp quyền <strong>Xem</strong>. Quyền <strong>Xuất</strong> cho phép tải dữ liệu ra file Excel/PDF.
      </div>

      {node.permissions.length === 0 && (
        <div className="rounded-xl border-2 border-dashed border-gray-200 py-8 text-center text-sm text-gray-400">
          Chưa có quyền truy cập nào — menu này sẽ bị ẩn với tất cả người dùng.
        </div>
      )}

      {node.permissions.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-gray-200">
          <table className="w-full text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-2.5 text-left text-xs font-semibold text-gray-500">Đối tượng</th>
                <th className="px-4 py-2.5 text-center text-xs font-semibold text-gray-500">Xem</th>
                <th className="px-4 py-2.5 text-center text-xs font-semibold text-gray-500">Xuất</th>
                <th className="w-10 px-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {node.permissions.map(p => (
                <tr key={p.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2">
                      <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        p.kind === 'role'
                          ? 'bg-purple-100 text-purple-700'
                          : 'bg-blue-100 text-blue-700'
                      }`}>
                        {p.kind === 'role' ? '🔐 Role' : '👤 User'}
                      </span>
                      <span className="font-medium text-gray-800">{p.label}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <button onClick={() => togglePerm(p.id, 'canView')}
                      className={`rounded-full w-6 h-6 text-xs font-bold transition-colors ${
                        p.canView ? 'bg-green-500 text-white' : 'bg-gray-200 text-gray-400'
                      }`}>
                      {p.canView ? '✓' : '✗'}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <button onClick={() => togglePerm(p.id, 'canExport')}
                      className={`rounded-full w-6 h-6 text-xs font-bold transition-colors ${
                        p.canExport ? 'bg-green-500 text-white' : 'bg-gray-200 text-gray-400'
                      }`}>
                      {p.canExport ? '✓' : '✗'}
                    </button>
                  </td>
                  <td className="px-2 py-3">
                    <button onClick={() => remove(p.id)}
                      className="rounded p-1 text-gray-300 hover:bg-red-50 hover:text-red-500 transition-colors">
                      <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showAdd ? (
        <div className="rounded-xl border border-brand-200 bg-brand-50 p-4 space-y-3">
          <p className="text-xs font-semibold text-brand-700">Thêm quyền truy cập</p>
          <div className="flex gap-2">
            <button onClick={() => setAddKind('role')}
              className={`rounded-lg px-3 py-1.5 text-xs font-medium transition-colors ${addKind === 'role' ? 'bg-brand-600 text-white' : 'bg-white text-gray-600 border border-gray-200'}`}>
              🔐 Theo Role
            </button>
            <button onClick={() => setAddKind('user')}
              className={`rounded-lg px-3 py-1.5 text-xs font-medium transition-colors ${addKind === 'user' ? 'bg-brand-600 text-white' : 'bg-white text-gray-600 border border-gray-200'}`}>
              👤 Theo User
            </button>
          </div>
          {addKind === 'role' ? (
            <div className="flex flex-wrap gap-2">
              {ROLE_OPTIONS.filter(r => !node.permissions.some(p => p.kind === 'role' && p.value === r)).map(r => (
                <button key={r} onClick={() => setAddValue(r)}
                  className={`rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors ${
                    addValue === r ? 'border-brand-400 bg-brand-100 text-brand-700' : 'border-gray-200 bg-white text-gray-600 hover:border-brand-300'
                  }`}>
                  {r}
                </button>
              ))}
            </div>
          ) : (
            <input
              value={addValue}
              onChange={e => setAddValue(e.target.value)}
              placeholder="Nhập username hoặc email..."
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            />
          )}
          <div className="flex gap-2">
            <button onClick={add}
              className="rounded-lg bg-brand-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-40"
              disabled={!addValue}>
              Thêm
            </button>
            <button onClick={() => { setShowAdd(false); setAddValue(''); }}
              className="rounded-lg px-4 py-1.5 text-xs text-gray-500 hover:bg-gray-100">
              Hủy
            </button>
          </div>
        </div>
      ) : (
        <button
          onClick={() => setShowAdd(true)}
          className="flex w-full items-center justify-center gap-2 rounded-xl border-2 border-dashed border-gray-200 py-3 text-sm text-gray-500 hover:border-brand-300 hover:text-brand-600 transition-colors"
        >
          <span className="text-lg">+</span> Thêm quyền truy cập
        </button>
      )}
    </div>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────────

type Tab = 'info' | 'reports' | 'permissions';

export function MenuManager() {
  const [menus, setMenus] = useState<MenuMap>(INIT_MENUS);
  const [rootIds, setRootIds] = useState<string[]>(ROOT_IDS);
  const [selectedId, setSelectedId] = useState<string | null>('1');
  const [expanded, setExpanded] = useState<Set<string>>(new Set(['2']));
  const [tab, setTab] = useState<Tab>('info');
  const [saved, setSaved] = useState(false);

  const selected = selectedId ? menus[selectedId] : null;

  const patch = (p: Partial<MenuNode>) => {
    if (!selectedId) return;
    setMenus(m => ({ ...m, [selectedId]: { ...m[selectedId], ...p } }));
    setSaved(false);
  };

  const save = () => { setSaved(true); setTimeout(() => setSaved(false), 2000); };

  const addMenu = () => {
    const id = uid();
    const newMenu: MenuNode = {
      id, name: 'Menu mới', slug: `menu-${id}`, icon: '📋',
      description: '', position: rootIds.length + 1, visible: true,
      parentId: null, reports: [], permissions: [], children: [],
    };
    setMenus(m => ({ ...m, [id]: newMenu }));
    setRootIds(r => [...r, id]);
    setSelectedId(id);
    setTab('info');
  };

  const deleteMenu = () => {
    if (!selectedId) return;
    const node = menus[selectedId];
    if (!window.confirm(`Xóa menu "${node.name}"? Hành động này không thể hoàn tác.`)) return;

    const next = { ...menus };
    delete next[selectedId];

    // Remove from parent or root
    if (node.parentId) {
      next[node.parentId] = { ...next[node.parentId], children: next[node.parentId].children.filter(c => c !== selectedId) };
    }
    setMenus(next);
    setRootIds(r => r.filter(id => id !== selectedId));
    setSelectedId(null);
  };

  const toggleExpand = (id: string) =>
    setExpanded(s => { const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n; });

  const tabClass = (t: Tab) =>
    `px-4 py-2.5 text-sm font-medium border-b-2 transition-colors ${
      tab === t
        ? 'border-brand-600 text-brand-700'
        : 'border-transparent text-gray-500 hover:text-gray-700'
    }`;

  const reportCount  = selected?.reports.filter(r => r.visible).length ?? 0;
  const permCount    = selected?.permissions.length ?? 0;

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Quản lý Menu Báo cáo</h1>
          <p className="mt-0.5 text-sm text-gray-500">Tạo, sắp xếp và phân quyền truy cập các nhóm báo cáo</p>
        </div>
        <div className="flex items-center gap-2">
          <span className="rounded-full bg-amber-100 px-3 py-1 text-xs font-medium text-amber-700">
            🧪 Mockup — chưa kết nối backend
          </span>
          <button onClick={addMenu}
            className="flex items-center gap-2 rounded-xl bg-brand-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-brand-700 transition-colors">
            <span className="text-lg leading-none">+</span> Tạo menu mới
          </button>
        </div>
      </div>

      <div className="flex flex-1 gap-6 overflow-hidden">

        {/* ── Left: Menu tree ──────────────────────────────────────────── */}
        <aside className="flex w-64 shrink-0 flex-col rounded-2xl border border-gray-200 bg-white shadow-sm overflow-hidden">
          <div className="border-b border-gray-100 px-4 py-3">
            <p className="text-xs font-semibold uppercase tracking-widest text-gray-400">Cây menu</p>
          </div>
          <div className="flex-1 overflow-y-auto p-3 space-y-0.5">
            {rootIds.map(id => (
              <TreeNode key={id} id={id} menus={menus} selectedId={selectedId}
                expanded={expanded} onSelect={id => { setSelectedId(id); setTab('info'); setSaved(false); }}
                onToggleExpand={toggleExpand} />
            ))}
          </div>
          <div className="border-t border-gray-100 p-3">
            <button onClick={addMenu}
              className="flex w-full items-center justify-center gap-1 rounded-lg border border-dashed border-gray-300 py-2 text-xs text-gray-500 hover:border-brand-400 hover:text-brand-600 transition-colors">
              + Thêm menu
            </button>
          </div>
        </aside>

        {/* ── Right: Editor ────────────────────────────────────────────── */}
        {selected ? (
          <div className="flex flex-1 flex-col rounded-2xl border border-gray-200 bg-white shadow-sm overflow-hidden">
            {/* Editor header */}
            <div className="flex items-center gap-3 border-b border-gray-100 px-6 py-4">
              <span className="text-2xl">{selected.icon}</span>
              <div className="flex-1">
                <h2 className="text-base font-bold text-gray-900">{selected.name}</h2>
                <p className="text-xs text-gray-400">/reports/{selected.slug}</p>
              </div>
              <div className="flex items-center gap-2">
                {/* Quick stats */}
                <span className="rounded-full bg-blue-50 px-2.5 py-1 text-xs text-blue-600">
                  {reportCount} báo cáo
                </span>
                <span className="rounded-full bg-purple-50 px-2.5 py-1 text-xs text-purple-600">
                  {permCount} quyền
                </span>
                <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${selected.visible ? 'bg-green-50 text-green-600' : 'bg-gray-100 text-gray-500'}`}>
                  {selected.visible ? '● Hiển thị' : '○ Ẩn'}
                </span>
              </div>
            </div>

            {/* Tabs */}
            <div className="flex border-b border-gray-100 px-6">
              <button className={tabClass('info')}      onClick={() => setTab('info')}>Thông tin</button>
              <button className={tabClass('reports')}   onClick={() => setTab('reports')}>
                Báo cáo
                {selected.reports.length > 0 && (
                  <span className="ml-1.5 rounded-full bg-gray-100 px-1.5 py-0.5 text-xs text-gray-600">
                    {selected.reports.length}
                  </span>
                )}
              </button>
              <button className={tabClass('permissions')} onClick={() => setTab('permissions')}>
                Phân quyền
                {permCount > 0 && (
                  <span className="ml-1.5 rounded-full bg-gray-100 px-1.5 py-0.5 text-xs text-gray-600">
                    {permCount}
                  </span>
                )}
              </button>
            </div>

            {/* Tab content */}
            <div className="flex-1 overflow-y-auto p-6" key={`${selectedId}-${tab}`}>
              {tab === 'info'        && <TabInfo        node={selected} onChange={patch} />}
              {tab === 'reports'     && <TabReports     node={selected} onChange={patch} />}
              {tab === 'permissions' && <TabPermissions node={selected} onChange={patch} />}
            </div>

            {/* Footer actions */}
            <div className="flex items-center justify-between border-t border-gray-100 px-6 py-4">
              <button onClick={deleteMenu}
                className="flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm text-red-500 hover:bg-red-50 transition-colors">
                <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
                Xóa menu
              </button>
              <div className="flex items-center gap-3">
                {saved && (
                  <span className="text-xs text-green-600 font-medium flex items-center gap-1">
                    <span>✓</span> Đã lưu
                  </span>
                )}
                <button onClick={save}
                  className="rounded-xl bg-brand-600 px-5 py-2 text-sm font-semibold text-white shadow-sm hover:bg-brand-700 transition-colors">
                  Lưu thay đổi
                </button>
              </div>
            </div>
          </div>
        ) : (
          <div className="flex flex-1 items-center justify-center rounded-2xl border-2 border-dashed border-gray-200 text-center">
            <div>
              <p className="text-4xl mb-3">📋</p>
              <p className="text-sm font-medium text-gray-500">Chọn một menu để chỉnh sửa</p>
              <p className="mt-1 text-xs text-gray-400">hoặc nhấn <strong>+ Tạo menu mới</strong></p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
