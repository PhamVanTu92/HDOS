import { useState, useCallback } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult } from '../api/requests';
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

  const submit = useCallback(
    async (operation: ReportOperation, params: Record<string, unknown>) => {
      setSubmitting(true);
      setSubmitError(null);
      setRequestId(null);
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

  const { data: result, isLoading: polling, error: pollError } = useQuery<
    RequestResult<T>
  >({
    queryKey: ['report-result', requestId],
    queryFn: () => getRequestResult<T>(requestId!),
    enabled: !!requestId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (!status) return 2000;
      return ['Completed', 'Failed', 'Cancelled'].includes(status) ? false : 2000;
    },
    staleTime: 0,
  });

  // Short-circuit polling when SignalR says we're done
  useSignalREvent(
    'RequestCompleted',
    (_payload) => {
      // react-query refetch cycle will pick it up automatically
    },
    !!requestId,
  );

  useSignalREvent(
    'RequestFailed',
    (_payload) => {
      // same — polling will reflect the status
    },
    !!requestId,
  );

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

  function reset() {
    setRequestId(null);
    setSubmitError(null);
  }

  return {
    submit,
    reset,
    status,
    progressPct,
    isRunning: submitting || (polling && !!requestId && status !== 'Completed' && status !== 'Failed' && status !== 'Cancelled'),
    data,
    error,
    requestId,
  };
}
