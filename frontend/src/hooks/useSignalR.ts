import { useEffect, useRef } from 'react';
import { useAuth } from 'react-oidc-context';
import {
  sseClient,
  type SseEventMap,
  type SseEventHandler,
} from '../api/sse';

// ── Re-exported type aliases for backward-compat with existing consumers ──────
export type SignalREventMap = SseEventMap;
export type SignalREventHandler<K extends keyof SignalREventMap> = SseEventHandler<K>;

/**
 * Opens the SSE event stream when the user is authenticated and closes it on
 * logout / unmount.  Also reconnects with a fresh token after every Keycloak
 * silent renewal so the backend always receives a valid JWT.
 *
 * Call this ONCE near the top of the component tree (e.g. Layout).
 */
export function useSignalRConnection() {
  const auth = useAuth();
  const connected = useRef(false);

  // ── Token refresh: reopen EventSource with the new token ───────────────────
  useEffect(() => {
    const onUserLoaded = () => {
      // Skip if we haven't established the first connection yet.
      if (!connected.current) return;
      // _open() closes the old EventSource and creates a new one with the
      // fresh token embedded in the URL query string.
      sseClient.reconnect();
    };

    auth.events.addUserLoaded(onUserLoaded);
    return () => {
      auth.events.removeUserLoaded(onUserLoaded);
    };
  }, [auth.events]);

  // ── SSE connection lifecycle ───────────────────────────────────────────────
  useEffect(() => {
    if (!auth.isAuthenticated || connected.current) return;

    connected.current = true;
    sseClient.connect();

    return () => {
      connected.current = false;
      sseClient.disconnect();
    };
  }, [auth.isAuthenticated]);
}

/**
 * Subscribe to an SSE event.  The handler reference is stable — it can be an
 * inline function.  Uses a ref internally so the subscription does not change
 * on every render even if the handler closure captures changing values.
 */
export function useSignalREvent<K extends keyof SignalREventMap>(
  event: K,
  handler: SignalREventHandler<K>,
  enabled = true,
) {
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(() => {
    if (!enabled) return;
    const stableHandler: SseEventHandler<K> = (payload) =>
      handlerRef.current(payload);
    return sseClient.on(event, stableHandler);
  }, [event, enabled]);
}

/**
 * Subscribe to WidgetStale events for a specific widget channel.
 * Tells the SSE client to include this channel when (re)opening the stream so
 * the server fans out WidgetStale events to this connection.
 */
export function useWidgetSubscription(
  channel: string,
  onStale?: SignalREventHandler<'WidgetStale'>,
  enabled = true,
) {
  useEffect(() => {
    if (!enabled || !channel) return;

    sseClient.subscribeWidget(channel);
    return () => {
      sseClient.unsubscribeWidget(channel);
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
