import { AuthProvider as OidcAuthProvider } from 'react-oidc-context';
import { type ReactNode } from 'react';

const authority = `${import.meta.env.VITE_KEYCLOAK_URL as string}/realms/${import.meta.env.VITE_KEYCLOAK_REALM as string}`;
const clientId  = import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string;

export const oidcConfig = {
  authority,
  client_id:                  clientId,
  redirect_uri:               window.location.origin,
  post_logout_redirect_uri:   window.location.origin,
  response_type:              'code',
  scope:                      'openid profile email',
  automaticSilentRenew:       true,
  loadUserInfo:               true,
};

export function AuthProvider({ children }: { children: ReactNode }) {
  return (
    <OidcAuthProvider {...oidcConfig}>
      {children}
    </OidcAuthProvider>
  );
}
