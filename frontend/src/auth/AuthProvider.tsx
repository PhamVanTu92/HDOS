import { AuthProvider as OidcAuthProvider, useAuth } from 'react-oidc-context';
import { type ReactNode, useEffect } from 'react';
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

/** Inner component — registers token providers once auth is available. */
function TokenRegistrar({ children }: { children: ReactNode }) {
  const auth = useAuth();

  useEffect(() => {
    const getToken = () => auth.user?.access_token ?? null;
    registerTokenProvider(getToken);
    registerSignalRTokenProvider(getToken);
  }, [auth.user]);

  return <>{children}</>;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  return (
    <OidcAuthProvider {...oidcConfig}>
      <TokenRegistrar>{children}</TokenRegistrar>
    </OidcAuthProvider>
  );
}
