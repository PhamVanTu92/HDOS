import { useState, useCallback } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult, mapPushToResult } from '../api/requests';
import { useSignalREvent } from './useSignalR';
import type {
  ReportOperation,
  RequestResult,
  Priority,
} from '../types/contracts';

interface UseReportOptions {
  priority?: Priority;
  cacheSeconds?: number;
}

export function useReport<T = unknown>(opts: UseReportOptions = {}) {
  const auth = useAuth();
  const userId = auth.user?.profile.sub ?? '';
  const tenantId =
    (auth.user?.profile['tenant_id'] as string | undefined) ?? userId;

  const [requestId, setRequestId] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // Terminal result delivered via SignalR push (primary path — no GET needed).
  const [pushed, setPushed] = useState<RequestResult<T> | null>(null);

  const submit = useCallback(
    async (operation: ReportOperation, params: Record<string, unknown>) => {
      setSubmitting(true);
      setSubmitError(null);
      setRequestId(null);
      setPushed(null);
      try {
        const ack = await submitRequest({
          operation,
          params,
          tenantId,
          userId,
          priority: opts.priority ?? 'Normal',
          cacheSeconds: opts.cacheSeconds,
        });
        setRequestId(ack.requestId);
      } catch (err) {
        setSubmitError(err instanceof Error ? err.message : String(err));
      } finally {
        setSubmitting(false);
      }
    },
    [tenantId, userId, opts.priority, opts.cacheSeconds],
  );

  // ── Primary path: consume the terminal result straight from the SignalR push ──
  // enabled: true — always registered so no event is missed when the push
  // arrives before the next render cycle (cache-hit race condition).
  // handlerRef in useSignalREvent keeps the requestId check current without
  // re-subscribing on every render.
  useSignalREvent(
    'RequestCompleted',
    (payload) => {
      if (payload.requestId === requestId) setPushed(mapPushToResult<T>(payload));
    },
    true,
  );
  useSignalREvent(
    'RequestFailed',
    (payload) => {
      if (payload.requestId === requestId) setPushed(mapPushToResult<T>(payload));
    },
    true,
  );

  // ── Fallback: poll GET only until a result arrives (covers a missed push on
  // reconnect). Disabled the moment the push delivers; slow 3s cadence otherwise.
  const { data: polled, error: pollError } = useQuery<RequestResult<T>>({
    queryKey: ['report-result', requestId],
    queryFn: () => getRequestResult<T>(requestId!),
    enabled: !!requestId && !pushed,
    refetchInterval: (query) => {
      if (pushed) return false;
      const status = query.state.data?.status;
      if (!status) return 3000;
      return ['Completed', 'Failed', 'Cancelled'].includes(status) ? false : 3000;
    },
    staleTime: 0,
  });

  const result = pushed ?? polled ?? null;
  const status = result?.status ?? (submitting ? 'Queued' : null);

  const progressPct = (() => {
    switch (status) {
      case 'Queued': return 15;
      case 'Processing': return 55;
      case 'Completed': return 100;
      case 'Failed':
      case 'Cancelled': return 100;
      default: return 0;
    }
  })();

  const error =
    submitError ??
    (result?.status === 'Failed' ? result.error ?? 'Report failed' : null) ??
    (pollError instanceof Error ? pollError.message : null);

  const data = result?.status === 'Completed' ? result.data ?? null : null;

  const isTerminal =
    status === 'Completed' || status === 'Failed' || status === 'Cancelled';

  function reset() {
    setRequestId(null);
    setSubmitError(null);
    setPushed(null);
  }

  return {
    submit,
    reset,
    status,
    progressPct,
    isRunning: submitting || (!!requestId && !isTerminal),
    data,
    error,
    requestId,
  };
}
