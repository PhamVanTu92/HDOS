/**
 * Fetch wrapper that attaches Bearer token from the OIDC user store.
 *
 * Reads the token directly from sessionStorage at call-time rather than via a
 * React closure.  oidc-client-ts writes the user to sessionStorage BEFORE it
 * fires the userLoaded event, so by the time any fetch (even one triggered
 * inside that event) executes, the token is already present in storage.
 *
 * This is race-condition-free: no registration step, no React render timing
 * dependency, no useEffect ordering issues.
 */

const GATEWAY = import.meta.env.VITE_GATEWAY_URL as string;

// Matches the key oidc-client-ts uses with its default SessionStorageStateStore.
const OIDC_STORAGE_KEY = `oidc.user:${
  import.meta.env.VITE_KEYCLOAK_URL as string
}/realms/${import.meta.env.VITE_KEYCLOAK_REALM as string}:${
  import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string
}`;

function getAccessToken(): string | null {
  try {
    const raw = sessionStorage.getItem(OIDC_STORAGE_KEY);
    if (!raw) return null;
    const user = JSON.parse(raw) as { access_token?: string };
    return user.access_token ?? null;
  } catch {
    return null;
  }
}

// Keep the registration API so other modules can still call it (no-op now).
// This avoids having to update every call-site immediately.
/** @deprecated Token is now read directly from sessionStorage. No-op. */
export function registerTokenProvider(_fn: () => string | null) {
  // intentionally empty — kept for backward compatibility
}

function buildHeaders(extra?: Record<string, string>): Record<string, string> {
  const token = getAccessToken();
  return {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...extra,
  };
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly body?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    // Read body as text first to avoid "body stream already read" error when
    // trying to call both res.json() and res.text() on the same response.
    let body: unknown;
    try {
      const text = await res.text();
      body = text ? JSON.parse(text) : null;
    } catch {
      body = null;
    }
    throw new ApiError(
      res.status,
      `HTTP ${res.status} ${res.statusText}`,
      body,
    );
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(`${GATEWAY}${path}`, {
    method: 'GET',
    headers: buildHeaders(),
  });
  return handleResponse<T>(res);
}

export async function apiPost<TBody, TResponse>(
  path: string,
  body: TBody,
): Promise<TResponse> {
  const res = await fetch(`${GATEWAY}${path}`, {
    method: 'POST',
    headers: buildHeaders(),
    body: JSON.stringify(body),
  });
  return handleResponse<TResponse>(res);
}
