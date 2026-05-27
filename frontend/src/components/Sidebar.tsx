import { type ReactNode, useState, useEffect } from 'react';
import { NavLink } from 'react-router-dom';
import { hasRealmRole, apiGet } from '../api/client';
import type { SidebarGroup } from '../types/module';
import type { MenuSummary } from '../types/menuTypes';

interface NavItem {
  to:    string;
  label: string;
  icon:  ReactNode;
  end?:  boolean;
}

// ── Icons ────────────────────────────────────────────────────────────────────

function BarChartIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
    </svg>
  );
}

function CogIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
    </svg>
  );
}

function ListIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 6h16M4 10h16M4 14h16M4 18h16" />
    </svg>
  );
}

function TerminalIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
    </svg>
  );
}

function SyncIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0" />
    </svg>
  );
}

function MenuIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h8m-8 6h16" />
    </svg>
  );
}

function LayoutIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z" />
    </svg>
  );
}

const ADMIN_ITEMS: NavItem[] = [
  { to: '/admin',            label: 'Quản trị',           icon: <CogIcon />,      end: true },
  { to: '/admin/modules',    label: 'Module Manager',     icon: <LayoutIcon /> },
  { to: '/admin/operations', label: 'Quản lý Operations', icon: <ListIcon /> },
  { to: '/admin/test',       label: 'Test Console',        icon: <TerminalIcon /> },
  { to: '/admin/sync',       label: 'Theo dõi đồng bộ',  icon: <SyncIcon /> },
  { to: '/admin/menus',      label: 'Quản lý Menu BC',    icon: <MenuIcon /> },
];

// ── Nav link helpers ──────────────────────────────────────────────────────────

function navCls(isActive: boolean, open: boolean, indent = false): string {
  return [
    'flex items-center gap-3 py-2 text-sm transition-colors',
    indent && open ? 'pl-8 pr-4' : 'px-4',
    isActive
      ? 'bg-[--brand] text-white'
      : 'text-[--tx2] hover:bg-[--overlay] hover:text-[--tx]',
  ].join(' ');
}

// ── Report nav items (legacy /reports/:menuSlug) ──────────────────────────────

function ReportNavItems({ menus, open }: { menus: MenuSummary[]; open: boolean }) {
  const roots = menus.filter(m => m.parentId === null).sort((a, b) => a.sortOrder - b.sortOrder);
  return (
    <>
      {roots.map(root => {
        const children = menus.filter(m => m.parentId === root.id).sort((a, b) => a.sortOrder - b.sortOrder);
        return (
          <div key={root.id}>
            <NavLink
              to={`/reports/${root.slug}`}
              className={({ isActive }) => navCls(isActive, open)}
            >
              <span className="shrink-0 text-base leading-none">{root.icon}</span>
              {open && <span className="truncate">{root.name}</span>}
            </NavLink>
            {open && children.map(child => (
              <NavLink
                key={child.id}
                to={`/reports/${child.slug}`}
                className={({ isActive }) => navCls(isActive, open, true)}
              >
                <span className="shrink-0 text-sm leading-none opacity-80">{child.icon}</span>
                <span className="truncate">{child.name}</span>
              </NavLink>
            ))}
          </div>
        );
      })}
    </>
  );
}

// ── Module group + module nav items ──────────────────────────────────────────

function ModuleNavItems({ groups, open }: { groups: SidebarGroup[]; open: boolean }) {
  return (
    <>
      {groups.map(group => (
        <div key={group.id}>
          {open && (
            <p className="mt-3 mb-1 px-4 hdos-section-label">{group.label}</p>
          )}
          {!open && <hr className="my-2 border-[--border]" />}
          {group.modules.map(mod => (
            <NavLink
              key={mod.id}
              to={`/m/${mod.slug}`}
              className={({ isActive }) => navCls(isActive, open)}
            >
              <span className="shrink-0 text-base leading-none w-5 text-center">
                {mod.icon ?? '▪'}
              </span>
              {open && <span className="truncate">{mod.label}</span>}
            </NavLink>
          ))}
        </div>
      ))}
    </>
  );
}

// ── Main Sidebar export ───────────────────────────────────────────────────────

interface SidebarProps {
  open:        boolean;
  onSignOut:   () => void;
  displayName: string;
}

export function Sidebar({ open, onSignOut, displayName }: SidebarProps) {
  const isAdmin = hasRealmRole('admin');
  const [moduleGroups, setModuleGroups] = useState<SidebarGroup[]>([]);
  const [reportMenus,  setReportMenus]  = useState<MenuSummary[]>([]);

  // Load config-driven modules
  useEffect(() => {
    apiGet<SidebarGroup[]>('/api/v1/modules')
      .then(data => setModuleGroups(data))
      .catch(() => { /* silent — sidebar just shows nothing */ });
  }, []);

  // Load legacy report menus
  useEffect(() => {
    apiGet<MenuSummary[]>('/api/v1/reports/menus')
      .then(data => setReportMenus(data))
      .catch(() => { /* silent */ });
  }, []);

  return (
    <>
      {/* Logo */}
      <div className="flex h-16 items-center gap-3 px-4 border-b border-dim flex-shrink-0">
        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg font-bold text-sm"
          style={{ background: 'var(--brand)' }}>
          HD
        </div>
        {open && <span className="text-sm font-semibold truncate text-white">HDOS Platform</span>}
      </div>

      {/* Nav */}
      <nav className="flex-1 overflow-y-auto py-3">
        {/* Dashboard — always first */}
        <NavLink
          to="/"
          end
          className={({ isActive }) =>
            `flex items-center gap-3 px-4 py-2.5 text-sm transition-colors ${
              isActive ? 'bg-[--brand] text-white' : 'text-[--tx2] hover:bg-[--overlay] hover:text-[--tx]'
            }`
          }
        >
          <span className="shrink-0"><BarChartIcon /></span>
          {open && <span>Dashboard</span>}
        </NavLink>

        {/* Config-driven module groups */}
        {moduleGroups.length > 0 && (
          <ModuleNavItems groups={moduleGroups} open={open} />
        )}

        {/* Legacy report menus */}
        {reportMenus.length > 0 && (
          <>
            {open && (
              <p className="mt-4 mb-1 px-4 hdos-section-label">Báo cáo</p>
            )}
            {!open && <hr className="my-2 border-[--border]" />}
            <ReportNavItems menus={reportMenus} open={open} />
          </>
        )}

        {/* Admin section */}
        {isAdmin && (
          <>
            {open ? (
              <p className="mt-4 mb-1 px-4 hdos-section-label">Quản trị</p>
            ) : (
              <hr className="my-3 border-[--border]" />
            )}
            {ADMIN_ITEMS.map(item => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  `flex items-center gap-3 py-2 text-sm transition-colors ${
                    open ? 'px-4' : 'px-4'
                  } ${isActive ? 'bg-[--brand] text-white' : 'text-[--tx2] hover:bg-[--overlay] hover:text-[--tx]'}`
                }
              >
                <span className="shrink-0 opacity-80">{item.icon}</span>
                {open && <span className="truncate">{item.label}</span>}
              </NavLink>
            ))}
          </>
        )}
      </nav>

      {/* User footer */}
      <div className="border-t border-dim p-4 flex-shrink-0">
        {open ? (
          <div>
            <p className="truncate text-xs font-medium text-white">{displayName}</p>
            <button
              onClick={onSignOut}
              className="mt-1 text-xs text-[--tx2] hover:text-[--tx] underline"
            >
              Sign out
            </button>
          </div>
        ) : (
          <button
            onClick={onSignOut}
            title="Sign out"
            className="flex h-8 w-8 items-center justify-center rounded-full bg-[--overlay] text-xs text-[--tx2] hover:bg-[--card-hover]"
          >
            {displayName.charAt(0).toUpperCase()}
          </button>
        )}
      </div>
    </>
  );
}
