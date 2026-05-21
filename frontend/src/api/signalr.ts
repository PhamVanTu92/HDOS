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

  private buildConnection(): HubConnection {
    return new HubConnectionBuilder()
      .withUrl(`${GATEWAY}/hubs/main`, {
        // Reads from sessionStorage at negotiation time — always fresh.
        accessTokenFactory: () => getAccessToken(),
      })
      .withHubProtocol(new MessagePackHubProtocol())
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
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

    // Re-register all current handlers on the new connection instance
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

    this.connection.onreconnected(() => {
      console.info('[SignalR] Reconnected');
    });

    await this.connection.start();
    console.info('[SignalR] Connected');
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
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeWidget', channel);
    }
  }

  async unsubscribeWidget(channel: string): Promise<void> {
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
