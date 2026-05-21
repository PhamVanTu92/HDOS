import { AuthProvider as OidcAuthProvider, useAuth } from 'react-oidc-context';
import { type ReactNode } from 'react';
import { registerTokenProvider } from '../api/client';
import { registerSignalRTokenProvider } from '../api/signalr';

const oidcConfig = {
  authority: `${import.meta.env.VITE_KEYCLOAK_URL as string}/realms/${import.meta.env.VITE_KEYCLOAK_REALM as string}`,
  client_id: import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string,
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile email',
  automaticSilentRenew: true,
  loadUserInfo: true,
};

/**
 * Registers token providers SYNCHRONOUSLY during render — không dùng useEffect.
 *
 * Lý do: useEffect chạy SAU render và SAU khi TanStack Query đã queue fetch.
 * Khi OIDC signinCallback xử lý xong, React re-render ngay với auth.user mới.
 * Nếu dùng useEffect, token chưa được register khi query fetch chạy → 401.
 *
 * Gọi registerTokenProvider trong render body đảm bảo token có sẵn
 * trước bất kỳ effect hay query fetch nào trong cùng render cycle.
 */
function TokenRegistrar({ children }: { children: ReactNode }) {
  const auth = useAuth();

  // Đây là side-effect trong render nhưng an toàn vì:
  // 1. Idempotent — ghi đè module-level singleton cùng giá trị
  // 2. Không gây re-render (không đụng React state)
  // 3. Production-safe (StrictMode double-invoke cũng OK)
  registerTokenProvider(() => auth.user?.access_token ?? null);
  registerSignalRTokenProvider(() => auth.user?.access_token ?? null);

  return <>{children}</>;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  return (
    <OidcAuthProvider {...oidcConfig}>
      <TokenRegistrar>{children}</TokenRegistrar>
    </OidcAuthProvider>
  );
}
