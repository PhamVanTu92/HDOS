import { Routes, Route } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { Layout } from './components/Layout';
import { Dashboard } from './pages/Dashboard';
import { ReportScreen } from './pages/ReportScreen';
import { Admin } from './pages/Admin';
import { ProviderOperations } from './pages/admin/ProviderOperations';
import { TestConsole } from './pages/admin/TestConsole';
import { DataSyncMonitor } from './pages/admin/DataSyncMonitor';
import { MenuManager } from './pages/admin/MenuManager';

// Handle the OIDC callback (silent renew / redirect back)
function OidcCallback() {
  const auth = useAuth();
  if (auth.isLoading) return <p className="p-8 text-center text-gray-500">Completing sign-in…</p>;
  if (auth.error) return <p className="p-8 text-center text-red-600">{auth.error.message}</p>;
  return null;
}

export default function App() {
  return (
    <Routes>
      {/* OIDC redirect landing — react-oidc-context handles it automatically */}
      <Route path="/callback" element={<OidcCallback />} />

      <Route
        path="/*"
        element={
          <ProtectedRoute>
            <Layout>
              <Routes>
                <Route path="/"                    element={<Dashboard />} />
                <Route path="/reports/:menuSlug"  element={<ReportScreen />} />
                {/* Admin sub-pages */}
                <Route path="/admin"              element={<Admin />} />
                <Route path="/admin/operations"   element={<ProviderOperations />} />
                <Route path="/admin/test"         element={<TestConsole />} />
                <Route path="/admin/sync"         element={<DataSyncMonitor />} />
                <Route path="/admin/menus"        element={<MenuManager />} />
              </Routes>
            </Layout>
          </ProtectedRoute>
        }
      />
    </Routes>
  );
}
