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
 * Call this once at a high level in the component tree (e.g. Layout).
 */
export function useSignalRConnection() {
  const auth = useAuth();
  const connected = useRef(false);

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
