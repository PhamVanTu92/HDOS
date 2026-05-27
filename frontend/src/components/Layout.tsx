import { type ReactNode, useState } from 'react';
import { useAuth } from 'react-oidc-context';
import { useSignalRConnection } from '../hooks/useSignalR';
import { Sidebar } from './Sidebar';

// ── Layout ────────────────────────────────────────────────────────────────────

export function Layout({ children }: { children: ReactNode }) {
  const auth          = useAuth();
  const [sidebarOpen, setSidebarOpen] = useState(true);

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
    <div className="flex h-screen overflow-hidden bg-[--bg]">
      {/* Sidebar */}
      <aside
        className={`flex flex-col bg-[--surface] text-white transition-all duration-300 ${
          sidebarOpen ? 'w-60' : 'w-16'
        }`}
        style={{ borderRight: '1px solid var(--border)' }}
      >
        <Sidebar open={sidebarOpen} onSignOut={handleSignOut} displayName={displayName} />
      </aside>

      {/* Main */}
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Header */}
        <header className="flex h-16 items-center gap-4 border-b border-dim bg-[--surface] px-6" style={{ boxShadow: '0 1px 0 var(--border)' }}>
          <button
            onClick={() => setSidebarOpen(!sidebarOpen)}
            className="rounded-md p-1.5 text-[--tx2] hover:bg-[--overlay]"
            aria-label="Toggle sidebar"
          >
            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>
          <div className="flex-1" />
          <span className="text-sm text-[--tx2]">{displayName}</span>
        </header>

        {/* Content */}
        <main className="flex-1 overflow-y-auto p-6 bg-[--bg]">{children}</main>
      </div>
    </div>
  );
}
