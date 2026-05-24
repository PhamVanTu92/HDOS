import { useState, useCallback } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult, mapPushToResult } from '../api/requests';
import { useSignalREvent, useWidgetSubscription } from './useSignalR';
import type {
  SalesTrend,
  SalesTrendParams,
  RequestResult,
  WidgetStaleEvent,
} from '../types/contracts';

// Same channel as Dashboard summary — the entire main-dashboard widget group
// is staled whenever excel-provider pushes a datasource.updated event.
const WIDGET_CHANNEL = 'widget:main-dashboard:main-dashboard';

function defaultParams(): SalesTrendParams {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 29); // last 30 days
  return {
    fromDate: from.toISOString().slice(0, 10),
    toDate: to.toISOString().slice(0, 10),
    groupBy: 'day',
  };
}

/**
 * Fetches Sales Trend data for the SalesChart widget.
 *
 * Primary path  : result delivered via SignalR RequestCompleted push.
 * Fallback path : GET /requests/{id}/result polled every 3 s (missed push).
 * Auto-refresh  : WidgetStale event on the main-dashboard channel triggers
 *                 a new submit so the chart stays current after data changes.
 */
export function useSalesTrend() {
  const auth = useAuth();
  const userId   = auth.user?.profile.sub ?? '';
  const tenantId =
    (auth.user?.profile['tenant_id'] as string | undefined) ?? userId;

  const [requestId,   setRequestId]   = useState<string | null>(null);
  const [submitting,  setSubmitting]  = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [pushed, setPushed] = useState<RequestResult<SalesTrend> | null>(null);

  const submit = useCallback(
    async (params?: Partial<SalesTrendParams>) => {
      if (submitting) return;
      setSubmitting(true);
      setSubmitError(null);
      setPushed(null);
      try {
        const p = { ...defaultParams(), ...params };
        const ack = await submitRequest({
          operation:    'report.sales.trend',
          params:       p,
          tenantId,
          userId,
          priority:     'Normal',
          cacheSeconds: 300,
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
        setPushed(mapPushToResult<SalesTrend>(payload));
    },
    true,
  );
  useSignalREvent(
    'RequestFailed',
    (payload) => {
      if (payload.requestId === requestId)
        setPushed(mapPushToResult<SalesTrend>(payload));
    },
    true,
  );

  // ── Fallback: GET polling until push arrives ───────────────────────────────
  const { data: polled, isLoading: resultLoading } =
    useQuery<RequestResult<SalesTrend>>({
      queryKey: ['sales-trend-result', requestId],
      queryFn:  () => getRequestResult<SalesTrend>(requestId!),
      enabled:  !!requestId && !pushed,
      refetchInterval: (query) => {
        if (pushed) return false;
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

  const result = pushed ?? polled ?? null;
  const trend  = result?.status === 'Completed' ? result.data ?? null : null;
  const isLoading = submitting || (!pushed && resultLoading);
  const error =
    submitError ??
    (result?.status === 'Failed' ? result.error ?? 'Request failed' : null);

  return { trend, isLoading, error, submit };
}
