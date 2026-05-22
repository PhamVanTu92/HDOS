import { type ReactNode, useState } from 'react';
import { NavLink } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useSignalRConnection } from '../hooks/useSignalR';
import type { User } from 'oidc-client-ts';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
}

function BarChartIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
    </svg>
  );
}

function DocumentIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
    </svg>
  );
}

function DatabaseIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7c0-1.657 3.582-3 8-3s8 1.343 8 3M4 7v5c0 1.657 3.582 3 8 3s8-1.343 8-3V7M4 7c0 1.657 3.582 3 8 3s8-1.343 8-3m0 10v-5" />
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

const NAV_ITEMS: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: <BarChartIcon /> },
  { to: '/reports', label: 'Báo cáo', icon: <DocumentIcon /> },
  { to: '/data', label: 'Quản lý dữ liệu', icon: <DatabaseIcon /> },
];

const ADMIN_NAV: NavItem = { to: '/admin', label: 'Admin', icon: <CogIcon /> };

function hasAdminRole(user: User | null | undefined): boolean {
  const profile = user?.profile as Record<string, unknown> | undefined;
  const realmAccess = profile?.realm_access as { roles?: string[] } | undefined;
  return realmAccess?.roles?.includes('admin') ?? false;
}

export function Layout({ children }: { children: ReactNode }) {
  const auth        = useAuth();
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const isAdmin     = hasAdminRole(auth.user);

  // Manages the global SignalR connection lifecycle
  useSignalRConnection();

  const displayName =
    auth.user?.profile.name ??
    auth.user?.profile.preferred_username ??
    'User';

  const handleSignOut = () => {
    void auth.signoutRedirect();
  };

  return (
    <div className="flex h-screen overflow-hidden bg-gray-50">
      {/* Sidebar */}
      <aside
        className={`flex flex-col bg-gray-900 text-white transition-all duration-300 ${
          sidebarOpen ? 'w-60' : 'w-16'
        }`}
      >
        {/* Logo */}
        <div className="flex h-16 items-center gap-3 px-4 border-b border-gray-700">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-brand-600 font-bold text-sm">
            HD
          </div>
          {sidebarOpen && (
            <span className="text-sm font-semibold truncate">
              HDOS Reporting
            </span>
          )}
        </div>

        {/* Nav */}
        <nav className="flex-1 overflow-y-auto py-4">
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 px-4 py-2.5 text-sm transition-colors ${
                  isActive
                    ? 'bg-brand-700 text-white'
                    : 'text-gray-300 hover:bg-gray-800 hover:text-white'
                }`
              }
            >
              <span className="shrink-0">{item.icon}</span>
              {sidebarOpen && <span>{item.label}</span>}
            </NavLink>
          ))}

          {/* Admin — only visible to users with 'admin' role */}
          {isAdmin && (
            <>
              {sidebarOpen && (
                <p className="mt-4 mb-1 px-4 text-xs font-semibold uppercase tracking-widest text-gray-500">
                  Quản trị
                </p>
              )}
              {!sidebarOpen && <hr className="my-3 border-gray-700" />}
              <NavLink
                to={ADMIN_NAV.to}
                className={({ isActive }) =>
                  `flex items-center gap-3 px-4 py-2.5 text-sm transition-colors ${
                    isActive
                      ? 'bg-brand-700 text-white'
                      : 'text-gray-300 hover:bg-gray-800 hover:text-white'
                  }`
                }
              >
                <span className="shrink-0">{ADMIN_NAV.icon}</span>
                {sidebarOpen && <span>{ADMIN_NAV.label}</span>}
              </NavLink>
            </>
          )}
        </nav>

        {/* User */}
        <div className="border-t border-gray-700 p-4">
          {sidebarOpen ? (
            <div>
              <p className="truncate text-xs font-medium text-white">{displayName}</p>
              <button
                onClick={handleSignOut}
                className="mt-1 text-xs text-gray-400 hover:text-white underline"
              >
                Sign out
              </button>
            </div>
          ) : (
            <button
              onClick={handleSignOut}
              title="Sign out"
              className="flex h-8 w-8 items-center justify-center rounded-full bg-gray-700 text-xs text-gray-300 hover:bg-gray-600"
            >
              {displayName.charAt(0).toUpperCase()}
            </button>
          )}
        </div>
      </aside>

      {/* Main */}
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Header */}
        <header className="flex h-16 items-center gap-4 border-b border-gray-200 bg-white px-6 shadow-sm">
          <button
            onClick={() => setSidebarOpen(!sidebarOpen)}
            className="rounded-md p-1.5 text-gray-500 hover:bg-gray-100"
            aria-label="Toggle sidebar"
          >
            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>
          <div className="flex-1" />
          <span className="text-sm text-gray-500">{displayName}</span>
        </header>

        {/* Content */}
        <main className="flex-1 overflow-y-auto p-6">{children}</main>
      </div>
    </div>
  );
}
