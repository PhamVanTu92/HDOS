import type {
  RequestCompletedEvent,
  RequestFailedEvent,
  RequestCancelledEvent,
  WidgetStaleEvent,
} from '../types/contracts';

const GATEWAY = import.meta.env.VITE_GATEWAY_URL as string;

// oidc-client-ts stores the user here before firing any events,
// so this is always populated when the SSE connection opens.
const OIDC_STORAGE_KEY = `oidc.user:${
  import.meta.env.VITE_KEYCLOAK_URL as string
}/realms/${import.meta.env.VITE_KEYCLOAK_REALM as string}:${
  import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string
}`;

function getAccessToken(): string {
  try {
    const raw = sessionStorage.getItem(OIDC_STORAGE_KEY);
    if (!raw) return '';
    const user = JSON.parse(raw) as { access_token?: string };
    return user.access_token ?? '';
  } catch {
    return '';
  }
}

// ── Event map (mirrors SignalREventMap so hooks are drop-in compatible) ──────

export type SseEventMap = {
  RequestCompleted: RequestCompletedEvent;
  RequestFailed: RequestFailedEvent;
  RequestCancelled: RequestCancelledEvent;
  WidgetStale: WidgetStaleEvent;
};

export type SseEventHandler<K extends keyof SseEventMap> = (
  payload: SseEventMap[K],
) => void;

type AnyHandler = (payload: unknown) => void;

/** Named SSE event types the backend publishes on GET /sse/events. */
const SSE_EVENT_NAMES = [
  'RequestCompleted',
  'RequestFailed',
  'RequestCancelled',
  'WidgetStale',
] as const;

// ── SSE client ───────────────────────────────────────────────────────────────

class SseClient {
  private eventSource: EventSource | null = null;
  private handlers = new Map<string, Set<AnyHandler>>();
  private subscribedWidgetChannels = new Set<string>();
  /**
   * True while the app wants the stream open.
   * connect() sets it; disconnect() clears it.
   * Prevents _open() from reconnecting after an intentional logout.
   */
  private shouldConnect = false;

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  connect(): void {
    if (this.shouldConnect) return;
    this.shouldConnect = true;
    this._open();
  }

  /**
   * Close and reopen with a fresh token.
   * Called by useSSEConnection after a silent Keycloak token renewal so the
   * backend receives a valid JWT without waiting for the old EventSource to fail.
   */
  reconnect(): void {
    if (this.shouldConnect) this._open();
  }

  disconnect(): void {
    this.shouldConnect = false;
    this.eventSource?.close();
    this.eventSource = null;
    console.info('[SSE] Disconnected');
  }

  /** True when the underlying EventSource has an open connection. */
  get isConnected(): boolean {
    return this.eventSource?.readyState === EventSource.OPEN;
  }

  // ── Event subscription ─────────────────────────────────────────────────────

  on<K extends keyof SseEventMap>(
    event: K,
    handler: SseEventHandler<K>,
  ): () => void {
    const h = handler as AnyHandler;
    if (!this.handlers.has(event)) {
      this.handlers.set(event, new Set());
    }
    this.handlers.get(event)!.add(h);

    return () => {
      this.handlers.get(event)?.delete(h);
    };
  }

  // ── Widget channel subscriptions ───────────────────────────────────────────

  /**
   * Subscribe to WidgetStale events for this channel.
   * Reconnects the EventSource so the new channel is included in the query params
   * that tell the server which widget groups to fan-out to this connection.
   */
  subscribeWidget(channel: string): void {
    if (this.subscribedWidgetChannels.has(channel)) return;
    this.subscribedWidgetChannels.add(channel);
    // Reconnect to register the new channel server-side.
    if (this.shouldConnect) this._open();
  }

  unsubscribeWidget(channel: string): void {
    if (!this.subscribedWidgetChannels.delete(channel)) return;
    // Reconnect without the removed channel.
    if (this.shouldConnect) this._open();
  }

  // ── Internal ───────────────────────────────────────────────────────────────

  private _open(): void {
    // Close any existing connection before opening a new one.
    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
    }

    const token = getAccessToken();
    if (!token) {
      console.warn('[SSE] No access token available — connection deferred');
      return;
    }

    // Build URL: token as query param (backend OnMessageReceived handles /sse/* paths),
    // plus any widget channels the app has subscribed to.
    const url = new URL(`${GATEWAY}/sse/events`);
    url.searchParams.set('access_token', token);
    for (const ch of this.subscribedWidgetChannels) {
      url.searchParams.append('widgetChannel', ch);
    }

    const es = new EventSource(url.toString());
    this.eventSource = es;

    es.onopen = () => {
      console.info('[SSE] Connected to /sse/events');
    };

    es.onerror = () => {
      // EventSource has built-in exponential back-off reconnection using the same URL.
      // That URL contains the token that was current at _open() time.  If the token
      // expired, the reconnect will 401 and EventSource will keep retrying.  The
      // useSSEConnection hook calls reconnect() on every Keycloak silent renewal, which
      // calls _open() with the fresh token — breaking the retry loop.
      console.warn('[SSE] Connection error — EventSource will retry automatically');
    };

    // Attach a named listener for each event type. The listeners call _dispatch()
    // which reads from this.handlers at call time, so handlers registered via on()
    // after _open() are picked up without another reconnect.
    for (const eventName of SSE_EVENT_NAMES) {
      es.addEventListener(eventName, (e: MessageEvent<string>) => {
        this._dispatch(eventName, e.data);
      });
    }
  }

  private _dispatch(eventName: string, dataJson: string): void {
    const handlers = this.handlers.get(eventName);
    if (!handlers?.size) return;
    try {
      const data = JSON.parse(dataJson) as unknown;
      for (const h of handlers) h(data);
    } catch {
      console.error('[SSE] Failed to parse event JSON:', eventName, dataJson);
    }
  }
}

/** Singleton SSE client shared across the entire app (replaces signalRClient). */
export const sseClient = new SseClient();
