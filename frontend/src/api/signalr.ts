import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import type {
  RequestCompletedEvent,
  RequestFailedEvent,
  RequestCancelledEvent,
  WidgetStaleEvent,
} from '../types/contracts';

const GATEWAY = import.meta.env.VITE_GATEWAY_URL as string;

// Same key as in client.ts — oidc-client-ts stores the user here before
// firing any events, so this is always populated when accessTokenFactory runs.
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

/**
 * @deprecated Token is now read directly from sessionStorage. No-op.
 * Kept for backward compatibility so call-sites don't need immediate updates.
 */
export function registerSignalRTokenProvider(_fn: () => string | null) {
  // intentionally empty
}

export type SignalREventMap = {
  RequestCompleted: RequestCompletedEvent;
  RequestFailed: RequestFailedEvent;
  RequestCancelled: RequestCancelledEvent;
  WidgetStale: WidgetStaleEvent;
};

export type SignalREventHandler<K extends keyof SignalREventMap> = (
  payload: SignalREventMap[K],
) => void;

type AnyHandler = (payload: unknown) => void;

class SignalRClient {
  private connection: HubConnection | null = null;
  private handlers = new Map<string, Set<AnyHandler>>();
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  /**
   * Channels that have been subscribed via SubscribeWidget.
   * Persisted across reconnections so we can re-invoke SubscribeWidget
   * after each auto-reconnect or explicit token-triggered reconnect.
   */
  private subscribedChannels = new Set<string>();

  private buildConnection(): HubConnection {
    return new HubConnectionBuilder()
      .withUrl(`${GATEWAY}/hubs/main`, {
        // Reads from sessionStorage at negotiation time — always fresh.
        // After a silent token renewal the new token is in sessionStorage
        // before this factory is called, so reconnects always get a valid JWT.
        accessTokenFactory: () => getAccessToken(),
      })
      .withHubProtocol(new MessagePackHubProtocol())
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
  }

  /** Re-invoke SubscribeWidget for every tracked channel. */
  private async resubscribeChannels(): Promise<void> {
    for (const ch of this.subscribedChannels) {
      try {
        await this.connection?.invoke('SubscribeWidget', ch);
      } catch (err) {
        console.warn(`[SignalR] Failed to re-subscribe channel "${ch}":`, err);
      }
    }
  }

  async connect(): Promise<void> {
    if (
      this.connection?.state === HubConnectionState.Connected ||
      this.connection?.state === HubConnectionState.Connecting
    ) {
      return;
    }

    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    this.connection = this.buildConnection();

    // Re-register all current handlers on the new connection instance.
    this.handlers.forEach((set, event) => {
      set.forEach((handler) => {
        this.connection!.on(event, handler);
      });
    });

    this.connection.onclose(() => {
      console.warn('[SignalR] Connection closed');
    });

    this.connection.onreconnecting(() => {
      console.info('[SignalR] Reconnecting…');
    });

    // After an auto-reconnect the server drops all group memberships.
    // Re-subscribe every tracked widget channel so WidgetStale events
    // keep flowing without any component needing to know about reconnects.
    this.connection.onreconnected(async () => {
      console.info('[SignalR] Reconnected — re-subscribing widget channels');
      await this.resubscribeChannels();
    });

    await this.connection.start();
    console.info('[SignalR] Connected');

    // Re-subscribe any widget channels that were registered before the
    // connection was established (e.g. useWidgetSubscription called during mount
    // before connect() finished).  onreconnected handles subsequent reconnects;
    // this call handles the first successful connection.
    await this.resubscribeChannels();
  }

  /**
   * Explicitly disconnect, then reconnect with a fresh JWT.
   * Called by useSignalRConnection after a silent token renewal so the hub
   * receives a valid token without waiting for the old one to expire.
   */
  async reconnect(): Promise<void> {
    if (
      this.connection &&
      this.connection.state !== HubConnectionState.Disconnected
    ) {
      await this.connection.stop();
    }
    this.connection = null;

    await this.connect();

    // connect() does not trigger onreconnected, so re-subscribe manually.
    await this.resubscribeChannels();

    console.info('[SignalR] Reconnected with refreshed token');
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  on<K extends keyof SignalREventMap>(
    event: K,
    handler: SignalREventHandler<K>,
  ): () => void {
    const h = handler as AnyHandler;
    if (!this.handlers.has(event)) {
      this.handlers.set(event, new Set());
    }
    this.handlers.get(event)!.add(h);
    this.connection?.on(event, h);

    return () => {
      this.handlers.get(event)?.delete(h);
      this.connection?.off(event, h);
    };
  }

  async subscribeWidget(channel: string): Promise<void> {
    this.subscribedChannels.add(channel);
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeWidget', channel);
    }
  }

  async unsubscribeWidget(channel: string): Promise<void> {
    this.subscribedChannels.delete(channel);
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('UnsubscribeWidget', channel);
    }
  }

  get state(): HubConnectionState | null {
    return this.connection?.state ?? null;
  }
}

/** Singleton shared across the app. */
export const signalRClient = new SignalRClient();
