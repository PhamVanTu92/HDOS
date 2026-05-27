/**
 * IconPickerModal — searchable grid of Lucide icons.
 * Used in AdminModuleList to pick a module icon.
 */

import { useState, useRef, useEffect } from 'react';
import { PICKABLE_ICONS, DynamicIcon } from './DynamicIcon';

interface IconPickerModalProps {
  current?: string;
  onSelect: (name: string) => void;
  onClose:  () => void;
}

export function IconPickerModal({ current, onSelect, onClose }: IconPickerModalProps) {
  const [search, setSearch]       = useState('');
  const [activeCategory, setActive] = useState<string | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => { searchRef.current?.focus(); }, []);

  // Close on Escape
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const lower = search.toLowerCase();
  const filtered = PICKABLE_ICONS.filter(
    ic => !lower || ic.name.toLowerCase().includes(lower) || ic.category.toLowerCase().includes(lower),
  );

  const categories = Array.from(new Set(filtered.map(ic => ic.category)));

  const byCategory: Record<string, typeof filtered> = {};
  for (const ic of filtered) {
    if (!byCategory[ic.category]) byCategory[ic.category] = [];
    byCategory[ic.category].push(ic);
  }

  // If filtering by category, restrict
  const displayCategories = activeCategory
    ? (byCategory[activeCategory] ? [activeCategory] : [])
    : categories;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60"
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        className="flex flex-col rounded-2xl overflow-hidden shadow-2xl"
        style={{
          width: '520px',
          maxHeight: '80vh',
          background: 'var(--overlay)',
          border: '1px solid var(--border-md)',
        }}
      >
        {/* Header */}
        <div
          className="flex items-center gap-3 px-5 py-4"
          style={{ borderBottom: '1px solid var(--border)' }}
        >
          <h3 className="text-sm font-bold flex-1" style={{ color: 'var(--tx)' }}>
            Chọn Icon
          </h3>
          <button onClick={onClose} style={{ color: 'var(--tx3)' }} className="hover:text-[--tx]">
            ✕
          </button>
        </div>

        {/* Search */}
        <div className="px-5 py-3" style={{ borderBottom: '1px solid var(--border)' }}>
          <input
            ref={searchRef}
            type="text"
            value={search}
            onChange={e => { setSearch(e.target.value); setActive(null); }}
            placeholder="Tìm icon theo tên..."
            className="hdos-input"
          />

          {/* Category chips */}
          {!search && (
            <div className="flex flex-wrap gap-1.5 mt-2.5">
              <button
                onClick={() => setActive(null)}
                className="px-2.5 py-0.5 rounded-full text-xs font-medium transition-colors"
                style={{
                  background: !activeCategory ? 'var(--brand)' : 'var(--surface)',
                  color:      !activeCategory ? '#fff' : 'var(--tx2)',
                  border:     '1px solid var(--border)',
                }}
              >
                Tất cả
              </button>
              {Array.from(new Set(PICKABLE_ICONS.map(i => i.category))).map(cat => (
                <button
                  key={cat}
                  onClick={() => setActive(cat === activeCategory ? null : cat)}
                  className="px-2.5 py-0.5 rounded-full text-xs font-medium transition-colors"
                  style={{
                    background: activeCategory === cat ? 'var(--brand)' : 'var(--surface)',
                    color:      activeCategory === cat ? '#fff' : 'var(--tx2)',
                    border:     '1px solid var(--border)',
                  }}
                >
                  {cat}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Icon grid */}
        <div className="flex-1 overflow-y-auto px-5 py-3 space-y-4">
          {displayCategories.length === 0 && (
            <div className="text-center py-8 text-sm" style={{ color: 'var(--tx3)' }}>
              Không tìm thấy icon nào
            </div>
          )}

          {displayCategories.map(cat => (
            <div key={cat}>
              {/* Category label */}
              {!search && (
                <p className="hdos-section-label mb-2">{cat}</p>
              )}
              <div className="grid grid-cols-8 gap-1.5">
                {byCategory[cat]?.map(ic => {
                  const isCurrent = ic.name === current;
                  return (
                    <button
                      key={ic.name}
                      onClick={() => onSelect(ic.name)}
                      title={ic.name}
                      className="flex flex-col items-center gap-1 p-2 rounded-lg transition-all"
                      style={{
                        background:   isCurrent ? 'var(--brand-dim)' : 'var(--surface)',
                        border:       `1px solid ${isCurrent ? 'var(--brand)' : 'var(--border)'}`,
                        color:        isCurrent ? 'var(--brand)' : 'var(--tx2)',
                      }}
                      onMouseEnter={e => {
                        if (!isCurrent) {
                          (e.currentTarget as HTMLElement).style.borderColor = 'var(--border-md)';
                          (e.currentTarget as HTMLElement).style.background  = 'var(--overlay)';
                          (e.currentTarget as HTMLElement).style.color       = 'var(--tx)';
                        }
                      }}
                      onMouseLeave={e => {
                        if (!isCurrent) {
                          (e.currentTarget as HTMLElement).style.borderColor = 'var(--border)';
                          (e.currentTarget as HTMLElement).style.background  = 'var(--surface)';
                          (e.currentTarget as HTMLElement).style.color       = 'var(--tx2)';
                        }
                      }}
                    >
                      <DynamicIcon name={ic.name} size={20} />
                      <span className="text-[9px] truncate w-full text-center leading-tight">
                        {ic.name.replace(/([A-Z])/g, ' $1').trim()}
                      </span>
                    </button>
                  );
                })}
              </div>
            </div>
          ))}
        </div>

        {/* Footer — current selection */}
        <div
          className="flex items-center justify-between px-5 py-3"
          style={{ borderTop: '1px solid var(--border)' }}
        >
          <div className="flex items-center gap-2 text-sm" style={{ color: 'var(--tx2)' }}>
            {current ? (
              <>
                <DynamicIcon name={current} size={18} />
                <span>{current}</span>
              </>
            ) : (
              <span style={{ color: 'var(--tx3)' }}>Chưa chọn icon</span>
            )}
          </div>
          <div className="flex gap-2">
            {current && (
              <button
                onClick={() => onSelect('')}
                className="btn-ghost text-xs"
              >
                Xóa icon
              </button>
            )}
            <button onClick={onClose} className="btn-ghost text-xs">
              Đóng
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
