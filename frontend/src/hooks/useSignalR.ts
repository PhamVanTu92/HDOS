import { useEffect, useRef } from 'react';
import { useAuth } from 'react-oidc-context';
import {
  signalRClient,
  type SignalREventMap,
  type SignalREventHandler,
} from '../api/signalr';

/**
 * Connects to the SignalR hub when the user is authenticated
 * and disconnects on logout / unmount.
 *
 * Also handles proactive token-aware reconnection:
 *   - oidc-client-ts fires `userLoaded` after every silent token renewal.
 *   - On that event we explicitly reconnect SignalR so the hub receives
 *     a fresh JWT before the old one expires, avoiding a transient 401
 *     that would otherwise force an unplanned auto-reconnect cycle.
 *
 * Call this ONCE at a high level in the component tree (e.g. Layout).
 */
export function useSignalRConnection() {
  const auth = useAuth();
  const connected = useRef(false);

  // ── Token auto-refresh: reconnect SignalR when Keycloak renews the token ──
  useEffect(() => {
    /**
     * Fired by oidc-client-ts after every successful silent renewal.
     * At this point sessionStorage already contains the new access_token,
     * so signalRClient.reconnect() picks it up via accessTokenFactory.
     */
    const onUserLoaded = () => {
      // Skip if we haven't established the first connection yet.
      if (!connected.current) return;
      signalRClient.reconnect().catch((err) => {
        console.warn('[SignalR] Reconnect after token refresh failed:', err);
      });
    };

    auth.events.addUserLoaded(onUserLoaded);
    return () => {
      auth.events.removeUserLoaded(onUserLoaded);
    };
  }, [auth.events]);

  // ── SignalR connection lifecycle ───────────────────────────────────────────
  useEffect(() => {
    if (!auth.isAuthenticated || connected.current) return;

    let cancelled = false;

    signalRClient.connect().then(() => {
      if (!cancelled) connected.current = true;
    }).catch((err) => {
      console.error('[SignalR] Failed to connect:', err);
    });

    return () => {
      cancelled = true;
      connected.current = false;
      signalRClient.disconnect();
    };
  }, [auth.isAuthenticated]);
}

/**
 * Subscribe to a SignalR event.
 * The handler reference is stable — it can be an inline function.
 */
export function useSignalREvent<K extends keyof SignalREventMap>(
  event: K,
  handler: SignalREventHandler<K>,
  enabled = true,
) {
  // Keep handler in a ref to avoid re-subscribing when it changes identity.
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(() => {
    if (!enabled) return;
    const stableHandler: SignalREventHandler<K> = (payload) =>
      handlerRef.current(payload);
    return signalRClient.on(event, stableHandler);
  }, [event, enabled]);
}

/** Subscribe to a widget channel and optionally handle WidgetStale. */
export function useWidgetSubscription(
  channel: string,
  onStale?: SignalREventHandler<'WidgetStale'>,
  enabled = true,
) {
  useEffect(() => {
    if (!enabled || !channel) return;

    signalRClient.subscribeWidget(channel);
    return () => {
      signalRClient.unsubscribeWidget(channel);
    };
  }, [channel, enabled]);

  useSignalREvent(
    'WidgetStale',
    (payload) => {
      if (payload.channel === channel && onStale) {
        onStale(payload);
      }
    },
    enabled,
  );
}
