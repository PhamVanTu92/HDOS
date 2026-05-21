/**
 * HTTP client — đính kèm Bearer token từ oidc-client-ts sessionStorage.
 *
 * oidc-client-ts ghi user vào sessionStorage TRƯỚC khi fire sự kiện userLoaded,
 * nên đọc từ storage lúc gọi là race-condition-free hoàn toàn.
 */

const GATEWAY = import.meta.env.VITE_GATEWAY_URL as string;

// Key oidc-client-ts dùng với SessionStorageStateStore mặc định:
// oidc.user:{authority}:{client_id}
const OIDC_KEY = [
  'oidc.user',
  `${import.meta.env.VITE_KEYCLOAK_URL as string}/realms/${import.meta.env.VITE_KEYCLOAK_REALM as string}`,
  import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string,
].join(':');

export function getAccessToken(): string | null {
  try {
    const raw = sessionStorage.getItem(OIDC_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { access_token?: string };
    return parsed.access_token ?? null;
  } catch {
    return null;
  }
}

/** @deprecated No-op — kept for backward compatibility */
export function registerTokenProvider(_fn: () => string | null): void { /* noop */ }

// ── Headers ──────────────────────────────────────────────────────────────────

function buildHeaders(extra?: Record<string, string>): Record<string, string> {
  const token = getAccessToken();
  return {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...extra,
  };
}

// ── Error ─────────────────────────────────────────────────────────────────────

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

// ── Response handler ──────────────────────────────────────────────────────────

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let body: unknown = null;
    try {
      const text = await res.text();
      body = text ? JSON.parse(text) : null;
    } catch { /* ignore */ }
    throw new ApiError(res.status, `HTTP ${res.status} ${res.statusText}`, body);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ── Public API ────────────────────────────────────────────────────────────────

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

export async function apiDelete(path: string): Promise<void> {
  const res = await fetch(`${GATEWAY}${path}`, {
    method: 'DELETE',
    headers: buildHeaders(),
  });
  await handleResponse<void>(res);
}
