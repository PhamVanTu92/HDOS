/**
 * Fetch wrapper that attaches Bearer token from the OIDC user store.
 * The token is retrieved lazily so we never capture a stale reference.
 */

const GATEWAY = import.meta.env.VITE_GATEWAY_URL as string;

let _getAccessToken: (() => string | null) | null = null;

/** Called once from AuthProvider after OIDC context is ready. */
export function registerTokenProvider(fn: () => string | null) {
  _getAccessToken = fn;
}

function buildHeaders(extra?: Record<string, string>): Record<string, string> {
  const token = _getAccessToken?.();
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
    // Đọc body dưới dạng text trước, tránh "body stream already read"
    // khi gọi res.json() rồi lại gọi res.text() trong catch
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
