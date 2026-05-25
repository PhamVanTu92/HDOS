import { useState, useCallback } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult, mapPushToResult } from '../api/requests';
import { useSignalREvent, useWidgetSubscription } from './useSignalR';
import type {
  RegionalPerformance,
  RegionPerformanceRow,
  RequestResult,
  WidgetStaleEvent,
  Period,
} from '../types/contracts';

// Same channel as Dashboard summary — staled on datasource.updated.
const WIDGET_CHANNEL = 'widget:main-dashboard:main-dashboard';

/**
 * Fetches Regional Performance data for the RegionTable widget.
 *
 * Primary path  : result delivered via SignalR RequestCompleted push.
 * Fallback path : GET /requests/{id}/result polled every 3 s (missed push).
 * Auto-refresh  : WidgetStale event on the main-dashboard channel triggers
 *                 a new submit so the table stays current after data changes.
 */
export function useRegionalPerformance() {
  const auth = useAuth();
  const userId   = auth.user?.profile.sub ?? '';
  const tenantId =
    (auth.user?.profile['tenant_id'] as string | undefined) ?? userId;

  const [requestId,   setRequestId]   = useState<string | null>(null);
  const [submitting,  setSubmitting]  = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [pushed, setPushed] = useState<RequestResult<RegionalPerformance> | null>(null);

  const submit = useCallback(
    async (period: Period = 'today') => {
      if (submitting) return;
      setSubmitting(true);
      setSubmitError(null);
      setPushed(null);
      try {
        const ack = await submitRequest({
          operation:    'report.regional.performance',
          params:       { period },
          tenantId,
          userId,
          priority:     'Normal',
          cacheSeconds: 60,
        });
        setRequestId(ack.requestId);
      } catch (err) {
        setSubmitError(err instanceof Error ? err.message : String(err));
      } finally {
        setSubmitting(false);
      }
    },
    [submitting, tenantId, userId],
  );

  // ── Primary: terminal result from SignalR push ─────────────────────────────
  // enabled: true — always registered so no event is missed when the push
  // arrives before the next render cycle (cache-hit race condition).
  useSignalREvent(
    'RequestCompleted',
    (payload) => {
      if (payload.requestId === requestId)
        setPushed(mapPushToResult<RegionalPerformance>(payload));
    },
    true,
  );
  useSignalREvent(
    'RequestFailed',
    (payload) => {
      if (payload.requestId === requestId)
        setPushed(mapPushToResult<RegionalPerformance>(payload));
    },
    true,
  );

  // ── Fallback: GET polling until push arrives ───────────────────────────────
  const { data: polled, isLoading: resultLoading } =
    useQuery<RequestResult<RegionalPerformance>>({
      queryKey: ['regional-performance-result', requestId],
      queryFn:  () => getRequestResult<RegionalPerformance>(requestId!),
      enabled:  !!requestId && !pushed,
      retry: false,
      refetchInterval: (query) => {
        if (pushed) return false;
        if (query.state.error) return false;
        const status = query.state.data?.status;
        if (!status) return 3000;
        return ['Completed', 'Failed', 'Cancelled'].includes(status)
          ? false
          : 3000;
      },
      staleTime: 0,
    });

  // ── Auto-refresh: re-submit when excel-provider pushes a data change ───────
  const handleWidgetStale = useCallback(
    (_payload: WidgetStaleEvent) => { submit(); },
    [submit],
  );
  useWidgetSubscription(WIDGET_CHANNEL, handleWidgetStale, true);

  const result  = pushed ?? polled ?? null;
  const regions: RegionPerformanceRow[] =
    result?.status === 'Completed' ? result.data?.regions ?? [] : [];
  const isLoading = submitting || (!pushed && resultLoading);
  const error =
    submitError ??
    (result?.status === 'Failed' ? result.error ?? 'Request failed' : null);

  return { regions, isLoading, error, submit };
}
