/**
 * AdminModuleList — Quản lý Module config-driven
 * Route: /admin/modules
 *
 * Chức năng:
 *  - Liệt kê tất cả module, nhóm theo group
 *  - Tạo module mới (inline modal)
 *  - Sửa thông tin module (inline modal, slug read-only)
 *  - Xóa module (confirm trước)
 *  - Điều hướng đến Dashboard Designer (/admin/modules/:slug/design)
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  listAdminModules,
  listModuleGroups,
  createModule,
  updateModule,
  deleteModule,
} from '../../api/adminModules';
import type { ModuleGroup } from '../../api/adminModules';
import type { AdminModule, UpsertModuleRequest } from '../../types/module';
import { DynamicIcon } from '../../components/DynamicIcon';
import { IconPickerModal } from '../../components/IconPickerModal';

// ── Form state ────────────────────────────────────────────────────────────────

interface ModuleFormState {
  groupId:               string;
  slug:                  string;
  label:                 string;
  icon:                  string;
  description:           string;
  sortOrder:             number;
  isVisible:             boolean;
  isActive:              boolean;
  refreshIntervalSeconds: string; // string để input number dễ xử lý
}

const EMPTY_FORM: ModuleFormState = {
  groupId:               '',
  slug:                  '',
  label:                 '',
  icon:                  '',
  description:           '',
  sortOrder:             0,
  isVisible:             true,
  isActive:              true,
  refreshIntervalSeconds: '',
};

function formToRequest(f: ModuleFormState): UpsertModuleRequest {
  return {
    groupId:               f.groupId,
    slug:                  f.slug.trim(),
    label:                 f.label.trim(),
    icon:                  f.icon.trim() || undefined,
    description:           f.description.trim() || undefined,
    sortOrder:             f.sortOrder,
    isVisible:             f.isVisible,
    isActive:              f.isActive,
    refreshIntervalSeconds: f.refreshIntervalSeconds
      ? Number(f.refreshIntervalSeconds)
      : undefined,
  };
}

function moduleToForm(m: AdminModule, groups: ModuleGroup[]): ModuleFormState {
  const group = groups.find(g => g.slug === m.groupSlug);
  return {
    groupId:               group?.id ?? '',
    slug:                  m.slug,
    label:                 m.label,
    icon:                  m.icon ?? '',
    description:           m.description ?? '',
    sortOrder:             m.sortOrder,
    isVisible:             m.isVisible,
    isActive:              m.isActive,
    refreshIntervalSeconds: m.refreshIntervalSeconds != null
      ? String(m.refreshIntervalSeconds)
      : '',
  };
}

// ── ModuleFormModal ───────────────────────────────────────────────────────────

interface ModuleFormModalProps {
  mode:      'create' | 'edit';
  initial:   ModuleFormState;
  groups:    ModuleGroup[];
  saving:    boolean;
  error:     string;
  onSave:    (f: ModuleFormState) => void;
  onClose:   () => void;
}

function ModuleFormModal({
  mode, initial, groups, saving, error, onSave, onClose,
}: ModuleFormModalProps) {
  const [form, setForm] = useState<ModuleFormState>(initial);
  const firstInputRef = useRef<HTMLInputElement>(null);
  const [showIconPicker, setShowIconPicker] = useState(false);

  // Focus first editable field on open
  useEffect(() => { firstInputRef.current?.focus(); }, []);

  const set = <K extends keyof ModuleFormState>(key: K, value: ModuleFormState[K]) =>
    setForm(prev => ({ ...prev, [key]: value }));

  const isCreate = mode === 'create';
  const canSubmit = form.groupId && form.slug.trim() && form.label.trim();

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60"
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="w-full max-w-lg rounded-2xl overflow-hidden shadow-2xl"
        style={{ background: 'var(--overlay)', border: '1px solid var(--border-md)' }}>

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4"
          style={{ borderBottom: '1px solid var(--border)' }}>
          <h3 className="text-base font-bold" style={{ color: 'var(--tx)' }}>
            {isCreate ? 'Tạo Module mới' : `Sửa Module — ${initial.slug}`}
          </h3>
          <button
            onClick={onClose}
            className="text-lg transition-colors"
            style={{ color: 'var(--tx3)' }}
            aria-label="Đóng"
          >
            ✕
          </button>
        </div>

        {/* Body */}
        <div className="px-6 py-5 space-y-4 max-h-[70vh] overflow-y-auto">

          {/* Group */}
          <div>
            <label className="hdos-section-label block mb-1.5">Nhóm module *</label>
            <select
              value={form.groupId}
              onChange={e => set('groupId', e.target.value)}
              className="hdos-input"
              style={{ background: 'var(--overlay)' }}
            >
              <option value="">-- Chọn nhóm --</option>
              {groups.map(g => (
                <option key={g.id} value={g.id}>
                  {g.icon ? `${g.icon} ` : ''}{g.label}
                </option>
              ))}
            </select>
          </div>

          {/* Slug */}
          <div>
            <label className="hdos-section-label block mb-1.5">Slug *</label>
            <input
              ref={isCreate ? firstInputRef : undefined}
              type="text"
              value={form.slug}
              onChange={e => set('slug', e.target.value)}
              readOnly={!isCreate}
              placeholder="vd: executive-dashboard"
              className="hdos-input font-mono"
              style={!isCreate ? { opacity: 0.55, cursor: 'not-allowed' } : {}}
            />
          </div>

          {/* Label */}
          <div>
            <label className="hdos-section-label block mb-1.5">Tên hiển thị *</label>
            <input
              ref={!isCreate ? firstInputRef : undefined}
              type="text"
              value={form.label}
              onChange={e => set('label', e.target.value)}
              placeholder="vd: Executive Dashboard"
              className="hdos-input"
            />
          </div>

          {/* Icon */}
          <div>
            <label className="hdos-section-label block mb-1.5">
              Icon <span style={{ color: 'var(--tx3)' }}>(tuỳ chọn)</span>
            </label>
            <button
              type="button"
              onClick={() => setShowIconPicker(true)}
              className="hdos-input flex items-center gap-2 text-left w-full"
              style={{ cursor: 'pointer', minHeight: 38 }}
            >
              {form.icon ? (
                <>
                  <DynamicIcon name={form.icon} size={18} className="shrink-0" />
                  <span className="flex-1 text-sm">{form.icon}</span>
                  <span
                    role="button"
                    onClick={e => { e.stopPropagation(); set('icon', ''); }}
                    className="shrink-0 text-xs px-1 rounded hover:bg-[--danger-bg] hover:text-[--danger]"
                    style={{ color: 'var(--tx3)' }}
                    title="Xóa icon"
                  >
                    ✕
                  </span>
                </>
              ) : (
                <span style={{ color: 'var(--tx3)' }}>Nhấn để chọn icon...</span>
              )}
            </button>
            {showIconPicker && (
              <IconPickerModal
                current={form.icon || undefined}
                onSelect={name => { set('icon', name); setShowIconPicker(false); }}
                onClose={() => setShowIconPicker(false)}
              />
            )}
          </div>

          {/* Description */}
          <div>
            <label className="hdos-section-label block mb-1.5">Mô tả <span style={{ color: 'var(--tx3)' }}>(tuỳ chọn)</span></label>
            <textarea
              value={form.description}
              onChange={e => set('description', e.target.value)}
              placeholder="Mô tả ngắn về module này..."
              rows={2}
              className="hdos-input resize-none"
            />
          </div>

          {/* Sort Order + Refresh Interval */}
          <div className="flex gap-4">
            <div className="flex-1">
              <label className="hdos-section-label block mb-1.5">Thứ tự</label>
              <input
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={e => set('sortOrder', Number(e.target.value))}
                className="hdos-input"
              />
            </div>
            <div className="flex-1">
              <label className="hdos-section-label block mb-1.5">
                Refresh <span style={{ color: 'var(--tx3)' }}>(giây)</span>
              </label>
              <input
                type="number"
                min={5}
                value={form.refreshIntervalSeconds}
                onChange={e => set('refreshIntervalSeconds', e.target.value)}
                placeholder="Để trống = không refresh"
                className="hdos-input"
              />
            </div>
          </div>

          {/* Toggle: isActive + isVisible */}
          <div className="flex gap-3">
            <button
              type="button"
              onClick={() => set('isActive', !form.isActive)}
              className="flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors"
              style={{
                border: `1px solid ${form.isActive ? 'rgba(46,204,113,0.35)' : 'var(--border-md)'}`,
                background: form.isActive ? 'var(--success-bg)' : 'transparent',
                color: form.isActive ? 'var(--success)' : 'var(--tx3)',
              }}
            >
              <span
                className="h-2 w-2 rounded-full"
                style={{ background: form.isActive ? 'var(--success)' : 'var(--tx3)' }}
              />
              {form.isActive ? 'Hoạt động' : 'Tắt'}
            </button>

            <button
              type="button"
              onClick={() => set('isVisible', !form.isVisible)}
              className="flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors"
              style={{
                border: `1px solid ${form.isVisible ? 'rgba(77,163,255,0.35)' : 'var(--border-md)'}`,
                background: form.isVisible ? 'var(--info-bg)' : 'transparent',
                color: form.isVisible ? 'var(--info)' : 'var(--tx3)',
              }}
            >
              {form.isVisible ? '👁 Hiển thị' : '🚫 Ẩn'}
            </button>
          </div>

          {/* Error */}
          {error && (
            <p className="text-sm rounded-lg px-3 py-2"
              style={{ color: 'var(--danger)', background: 'var(--danger-bg)' }}>
              {error}
            </p>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 px-6 py-4"
          style={{ borderTop: '1px solid var(--border)' }}>
          <button onClick={onClose} className="btn-ghost">Hủy</button>
          <button
            onClick={() => onSave(form)}
            disabled={!canSubmit || saving}
            className="btn-brand"
          >
            {saving
              ? (isCreate ? 'Đang tạo…' : 'Đang lưu…')
              : (isCreate ? '+ Tạo Module' : 'Lưu thay đổi')}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── ModuleCard ────────────────────────────────────────────────────────────────

interface ModuleCardProps {
  module:    AdminModule;
  onDesign:  () => void;
  onEdit:    () => void;
  onDelete:  () => void;
}

function ModuleCard({ module: m, onDesign, onEdit, onDelete }: ModuleCardProps) {
  return (
    <div className="hdos-card p-4 flex flex-col gap-3 transition-all"
      style={{ '--card-hover-border': 'var(--border-md)' } as React.CSSProperties}>

      {/* Top row: icon + label + slug + badge */}
      <div className="flex items-start gap-3">
        {m.icon && (
          <span className="shrink-0 mt-0.5 flex items-center justify-center w-6 h-6" style={{ color: 'var(--brand)' }}>
            <DynamicIcon name={m.icon} size={22} />
          </span>
        )}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-semibold text-sm" style={{ color: 'var(--tx)' }}>
              {m.label}
            </span>
            <span className="font-mono text-xs px-1.5 py-0.5 rounded"
              style={{ background: 'var(--surface)', color: 'var(--tx3)', border: '1px solid var(--border)' }}>
              {m.slug}
            </span>
            <span className={`badge ${m.isActive ? 'badge-success' : 'badge-danger'}`}>
              {m.isActive ? 'Hoạt động' : 'Ẩn'}
            </span>
            {!m.isVisible && (
              <span className="badge badge-neutral">Ẩn sidebar</span>
            )}
          </div>

          {m.description && (
            <p className="text-xs mt-1 line-clamp-2" style={{ color: 'var(--tx2)' }}>
              {m.description}
            </p>
          )}

          {m.requiredRoles && m.requiredRoles.length > 0 && (
            <div className="flex flex-wrap gap-1 mt-1.5">
              {m.requiredRoles.map(role => (
                <span key={role}
                  className="text-[10px] px-1.5 py-0.5 rounded font-mono"
                  style={{ background: 'var(--brand-dim)', color: 'var(--info)' }}>
                  {role}
                </span>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Action buttons */}
      <div className="flex gap-2 flex-wrap">
        <button onClick={onDesign} className="btn-brand text-xs py-1.5 px-3 gap-1.5">
          🎨 Design Canvas
        </button>
        <button onClick={onEdit} className="btn-ghost text-xs py-1.5 px-3">
          ✏ Sửa
        </button>
        <button onClick={onDelete} className="btn-danger text-xs py-1.5 px-3">
          🗑 Xóa
        </button>
      </div>
    </div>
  );
}

// ── Skeleton ──────────────────────────────────────────────────────────────────

function SkeletonGroup() {
  return (
    <div className="space-y-2">
      <div className="hdos-skeleton h-4 w-32 mb-3" />
      {[1, 2].map(i => (
        <div key={i} className="hdos-card p-4 space-y-2">
          <div className="flex items-center gap-3">
            <div className="hdos-skeleton h-6 w-6 rounded" />
            <div className="hdos-skeleton h-4 w-40" />
            <div className="hdos-skeleton h-4 w-20" />
          </div>
          <div className="hdos-skeleton h-3 w-2/3" />
          <div className="flex gap-2">
            <div className="hdos-skeleton h-7 w-32 rounded-lg" />
            <div className="hdos-skeleton h-7 w-16 rounded-lg" />
            <div className="hdos-skeleton h-7 w-14 rounded-lg" />
          </div>
        </div>
      ))}
    </div>
  );
}

// ── AdminModuleList ───────────────────────────────────────────────────────────

export function AdminModuleList() {
  const navigate = useNavigate();

  // ─ Data ──
  const [modules, setModules] = useState<AdminModule[]>([]);
  const [groups,  setGroups]  = useState<ModuleGroup[]>([]);

  // ─ UI state ──
  const [loading,     setLoading]     = useState(true);
  const [saving,      setSaving]      = useState(false);
  const [loadError,   setLoadError]   = useState('');
  const [formError,   setFormError]   = useState('');

  // ─ Modal state ──
  type ModalMode = { mode: 'create'; form: ModuleFormState } | { mode: 'edit'; form: ModuleFormState; slug: string } | null;
  const [modal, setModal] = useState<ModalMode>(null);

  // Ref to avoid stale-closure issues in callbacks
  const groupsRef = useRef<ModuleGroup[]>([]);
  groupsRef.current = groups;

  // ─ Load data ──
  const load = useCallback(async () => {
    setLoading(true);
    setLoadError('');
    try {
      const [mods, grps] = await Promise.all([listAdminModules(), listModuleGroups()]);
      setModules(mods);
      setGroups(grps);
    } catch (e) {
      setLoadError('Không thể tải danh sách module: ' + (e instanceof Error ? e.message : String(e)));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  // ─ Group modules by groupLabel ──
  const grouped = (() => {
    const map = new Map<string, AdminModule[]>();
    for (const m of modules) {
      const key = m.groupLabel;
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(m);
    }
    // Sort modules within each group by sortOrder
    for (const [, arr] of map) arr.sort((a, b) => a.sortOrder - b.sortOrder);
    // Sort groups by the first module's sortOrder (or alphabetically if no modules)
    return Array.from(map.entries()).sort((a, b) => {
      const aMin = Math.min(...a[1].map(m => m.sortOrder));
      const bMin = Math.min(...b[1].map(m => m.sortOrder));
      return aMin - bMin;
    });
  })();

  // ─ Open create modal ──
  const openCreate = () => {
    setFormError('');
    setModal({ mode: 'create', form: { ...EMPTY_FORM } });
  };

  // ─ Open edit modal ──
  const openEdit = (m: AdminModule) => {
    setFormError('');
    setModal({ mode: 'edit', form: moduleToForm(m, groupsRef.current), slug: m.slug });
  };

  // ─ Close modal ──
  const closeModal = () => { setModal(null); setFormError(''); };

  // ─ Handle save (create or edit) ──
  const handleSave = async (form: ModuleFormState) => {
    if (!modal) return;
    setFormError('');
    setSaving(true);
    try {
      if (modal.mode === 'create') {
        await createModule(formToRequest(form));
      } else {
        const { groupId, slug: _slug, ...rest } = formToRequest(form);
        await updateModule(modal.slug, { groupId, ...rest });
      }
      closeModal();
      await load();
    } catch (e) {
      setFormError(
        modal.mode === 'create'
          ? 'Lỗi tạo module: ' + (e instanceof Error ? e.message : String(e))
          : 'Lỗi cập nhật module: ' + (e instanceof Error ? e.message : String(e)),
      );
    } finally {
      setSaving(false);
    }
  };

  // ─ Handle delete ──
  const handleDelete = async (m: AdminModule) => {
    if (!window.confirm(`Xóa module "${m.label}" (${m.slug})?\nThao tác này không thể hoàn tác.`)) return;
    try {
      await deleteModule(m.slug);
      await load();
    } catch (e) {
      // Show inline toast-style alert — simple alert() acceptable here
      alert('Lỗi xóa module: ' + (e instanceof Error ? e.message : String(e)));
    }
  };

  // ─ Navigate to design canvas ──
  const handleDesign = (slug: string) => navigate(`/admin/modules/${slug}/design`);

  // ── Render ────────────────────────────────────────────────────────────────────
  return (
    <div className="flex flex-col gap-6">

      {/* Header */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div>
          <h1 className="text-xl font-bold" style={{ color: 'var(--tx)' }}>
            Module Manager
          </h1>
          <p className="text-sm mt-0.5" style={{ color: 'var(--tx2)' }}>
            Quản lý các module config-driven — tạo, chỉnh sửa và thiết kế dashboard
          </p>
        </div>
        <button onClick={openCreate} className="btn-brand shrink-0">
          + Tạo Module mới
        </button>
      </div>

      {/* Load error */}
      {loadError && (
        <div className="rounded-lg px-4 py-3 text-sm"
          style={{ background: 'var(--danger-bg)', color: 'var(--danger)', border: '1px solid rgba(255,82,82,0.25)' }}>
          {loadError}
          <button onClick={() => void load()} className="ml-3 underline text-xs">
            Thử lại
          </button>
        </div>
      )}

      {/* Loading skeleton */}
      {loading && (
        <div className="space-y-8">
          <SkeletonGroup />
          <SkeletonGroup />
        </div>
      )}

      {/* Empty state */}
      {!loading && !loadError && modules.length === 0 && (
        <div className="hdos-card flex flex-col items-center justify-center py-16 gap-3">
          <span className="text-4xl">📦</span>
          <p className="text-sm font-medium" style={{ color: 'var(--tx2)' }}>
            Chưa có module nào
          </p>
          <p className="text-xs" style={{ color: 'var(--tx3)' }}>
            Nhấn <strong style={{ color: 'var(--tx2)' }}>+ Tạo Module mới</strong> để bắt đầu
          </p>
        </div>
      )}

      {/* Module groups */}
      {!loading && grouped.map(([groupLabel, mods]) => (
        <section key={groupLabel} className="space-y-2">
          {/* Group header */}
          <div className="flex items-center gap-2 mb-3">
            <span className="hdos-section-label">● {groupLabel}</span>
            <span className="text-xs rounded-full px-2 py-0.5"
              style={{ background: 'var(--surface)', color: 'var(--tx3)', border: '1px solid var(--border)' }}>
              {mods.length}
            </span>
          </div>

          {/* Module cards */}
          <div className="flex flex-col gap-2">
            {mods.map(m => (
              <ModuleCard
                key={m.id}
                module={m}
                onDesign={() => handleDesign(m.slug)}
                onEdit={() => openEdit(m)}
                onDelete={() => void handleDelete(m)}
              />
            ))}
          </div>
        </section>
      ))}

      {/* Modal */}
      {modal && (
        <ModuleFormModal
          mode={modal.mode}
          initial={modal.form}
          groups={groups}
          saving={saving}
          error={formError}
          onSave={form => void handleSave(form)}
          onClose={closeModal}
        />
      )}
    </div>
  );
}
