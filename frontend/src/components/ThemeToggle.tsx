/**
 * ThemeToggle — dark / light theme switcher.
 * Persists to localStorage. Applies data-theme to <html>.
 * Module-level init prevents FOUC on page load.
 */

import { useEffect, useState } from 'react';

// ── Init at module load time (prevents FOUC) ──────────────────────────────────
(function initTheme() {
  try {
    const stored = localStorage.getItem('hdos-theme') ?? 'dark';
    document.documentElement.setAttribute('data-theme', stored);
  } catch { /* localStorage may be blocked */ }
})();

// ── Inline SVG icons ──────────────────────────────────────────────────────────

function SunIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"
      strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41" />
    </svg>
  );
}

function MoonIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"
      strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z" />
    </svg>
  );
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ThemeToggle() {
  const [isDark, setIsDark] = useState<boolean>(() => {
    try { return (localStorage.getItem('hdos-theme') ?? 'dark') === 'dark'; }
    catch { return true; }
  });

  useEffect(() => {
    const theme = isDark ? 'dark' : 'light';
    document.documentElement.setAttribute('data-theme', theme);
    try { localStorage.setItem('hdos-theme', theme); } catch { /* ignore */ }
  }, [isDark]);

  return (
    <button
      onClick={() => setIsDark(d => !d)}
      className="rounded-md p-1.5 transition-colors"
      style={{ color: 'var(--tx2)' }}
      onMouseEnter={e => (e.currentTarget.style.background = 'var(--overlay)')}
      onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
      aria-label={isDark ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'}
      title={isDark ? 'Giao diện sáng' : 'Giao diện tối'}
    >
      {isDark ? <SunIcon /> : <MoonIcon />}
    </button>
  );
}
