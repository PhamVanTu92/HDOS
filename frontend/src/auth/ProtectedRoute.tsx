import { useAuth } from 'react-oidc-context';
import { type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

export function ProtectedRoute({ children }: Props) {
  const auth = useAuth();

  if (auth.isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-gray-50">
        <div className="text-center">
          <div className="mx-auto mb-4 h-12 w-12 animate-spin rounded-full border-4 border-brand-600 border-t-transparent" />
          <p className="text-gray-600">Authenticating…</p>
        </div>
      </div>
    );
  }

  if (auth.error) {
    return (
      <div className="flex h-screen items-center justify-center bg-gray-50">
        <div className="rounded-lg border border-red-200 bg-red-50 p-6 text-center">
          <p className="mb-2 font-semibold text-red-700">Authentication Error</p>
          <p className="mb-4 text-sm text-red-600">{auth.error.message}</p>
          <button
            onClick={() => auth.signinRedirect()}
            className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700"
          >
            Try Again
          </button>
        </div>
      </div>
    );
  }

  if (!auth.isAuthenticated) {
    return (
      <div className="flex h-screen items-center justify-center bg-gray-50">
        <div className="rounded-xl border border-gray-200 bg-white p-10 text-center shadow-lg">
          <h1 className="mb-2 text-2xl font-bold text-gray-800">
            HDOS Reporting Platform
          </h1>
          <p className="mb-6 text-gray-500">
            Sign in with your organisational account to continue.
          </p>
          <button
            onClick={() => auth.signinRedirect()}
            className="rounded-md bg-brand-600 px-6 py-2.5 text-sm font-semibold text-white hover:bg-brand-700 focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-2"
          >
            Sign In
          </button>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
