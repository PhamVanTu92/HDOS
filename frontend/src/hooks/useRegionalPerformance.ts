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
 * Adapts the RENDER_CONTRACTS bar_chart payload returned by the new RegionalPerformanceHandler
 * into the legacy RegionPerformanceRow[] shape expected by RegionTable.tsx.
 *
 * New format: { series: [{name:"Thực tế",data:[{x,y}]}, {name:"Mục tiêu",data:[{x,y}]}] }
 * Legacy format: [{ name, revenue, units, target, achievementPct }]
 *
 * Note: `units` is not available in the new format and defaults to 0.
 */
function parseBarChart(raw: unknown): RegionPerformanceRow[] {
  if (!raw || typeof raw !== 'object') return [];
  const r = raw as Record<string, unknown>;

  // Old legacy format
  if (Array.isArray(r.regions)) return r.regions as RegionPerformanceRow[];

  // New RENDER_CONTRACTS bar_chart format
  if (Array.isArray(r.series)) {
    type RcSeries = { name: string; data: Array<{ x: unknown; y: number }> };
    const series = r.series as RcSeries[];
    const actual = series.find((s) => s.name === 'Thực tế');
    const target = series.find((s) => s.name === 'Mục tiêu');
    if (!actual) return [];

    return actual.data.map((pt, i) => {
      const actualVal = pt.y;
      const targetVal = target?.data[i]?.y ?? 0;
      const achievementPct =
        targetVal > 0 ? Math.round((actualVal / targetVal) * 100) : 0;
      return {
        name:           String(pt.x),
        revenue:        actualVal,
        units:          0,   // not returned by bar_chart handler
        target:         targetVal,
        achievementPct,
      };
    });
  }

  return [];
}

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
  const regions: RegionPerformanceRow[] = parseBarChart(
    result?.status === 'Completed' ? (result.data as unknown) ?? null : null,
  );
  const isLoading = submitting || (!pushed && resultLoading);
  const error =
    submitError ??
    (result?.status === 'Failed' ? result.error ?? 'Request failed' : null);

  return { regions, isLoading, error, submit };
}
