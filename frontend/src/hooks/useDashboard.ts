import { useState, useCallback } from 'react';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';
import { submitRequest, getRequestResult, mapPushToResult } from '../api/requests';
import { useSignalREvent, useWidgetSubscription } from './useSignalR';
import type {
  DashboardSummary,
  RequestResult,
  WidgetStaleEvent,
} from '../types/contracts';

// Must match the dashboard/widget registered in event_subscriptions
// (DashboardCode=main-dashboard, WidgetId=main-dashboard) so WidgetStale lands here.
const WIDGET_ID = 'main-dashboard';

/**
 * Adapts the RENDER_CONTRACTS kpi_grid payload returned by the new DashboardSummaryHandler
 * into the legacy DashboardSummary shape expected by Dashboard.tsx.
 *
 * New format: { columns: 3, items: [{id, label, value, format, ...}] }
 * Legacy format: { totalRevenue, totalUnits, topRegion, topProduct, alerts[] }
 */
function parseKpiGrid(raw: unknown): DashboardSummary | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;

  // New RENDER_CONTRACTS kpi_grid format (items array present)
  if (Array.isArray(r.items)) {
    type KpiItem = { id: string; value: unknown };
    const items = r.items as KpiItem[];
    const find = (id: string) => items.find((it) => it.id === id)?.value;

    const stockAlertCount = Number(find('stock_alerts') ?? 0);
    return {
      totalRevenue: Number(find('total_revenue') ?? 0),
      totalUnits:   Number(find('total_units')   ?? 0),
      topRegion:    String(find('top_region')    ?? '—'),
      topProduct:   '—',
      alerts: stockAlertCount > 0
        ? [`${stockAlertCount} sản phẩm tồn kho thấp`]
        : [],
    };
  }

  // Fallback: already in legacy shape (e.g. cached response from old handler)
  return raw as DashboardSummary;
}
const DASHBOARD_CHANNEL = `widget:main-dashboard:${WIDGET_ID}`;

function tenantFromToken(sub: string | undefined): string {
  // Tenant id comes from the JWT "tenant_id" claim; fall back to a safe default.
  return sub ?? 'default';
}

export function useDashboard() {
  const auth = useAuth();
  const userId = auth.user?.profile.sub ?? '';
  // Keycloak puts tenant_id as a custom claim
  const tenantId =
    (auth.user?.profile['tenant_id'] as string | undefined) ??
    tenantFromToken(userId);

  const [requestId, setRequestId] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // Terminal result delivered via SignalR push (primary path — no GET needed).
  const [pushed, setPushed] = useState<RequestResult<DashboardSummary> | null>(null);

  const submit = useCallback(
    async (date?: string) => {
      if (submitting) return;
      setSubmitting(true);
      setSubmitError(null);
      setPushed(null);
      try {
        const ack = await submitRequest({
          operation: 'report.dashboard.summary',
          params: date ? { date } : {},
          tenantId,
          userId,
          priority: 'High',
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

  // ── Primary: terminal result straight from the SignalR push ──────────────────
  // enabled: true — always registered so no event is missed when the push
  // arrives before the next render cycle (cache-hit race condition).
  // handlerRef keeps the requestId check current without re-subscribing.
  useSignalREvent(
    'RequestCompleted',
    (payload) => {
      if (payload.requestId === requestId)
        setPushed(mapPushToResult<DashboardSummary>(payload));
    },
    true,
  );
  useSignalREvent(
    'RequestFailed',
    (payload) => {
      if (payload.requestId === requestId)
        setPushed(mapPushToResult<DashboardSummary>(payload));
    },
    true,
  );

  // ── Fallback: poll GET only until a result arrives (missed push on reconnect) ──
  const { data: polled, isLoading: resultLoading, error: resultError } =
    useQuery<RequestResult<DashboardSummary>>({
      queryKey: ['dashboard-result', requestId],
      queryFn: () => getRequestResult<DashboardSummary>(requestId!),
      enabled: !!requestId && !pushed,
      retry: false,
      refetchInterval: (query) => {
        if (pushed) return false;
        if (query.state.error) return false;          // stop on any error (e.g. 404 expired)
        const status = query.state.data?.status;
        if (!status) return 3000;
        return ['Completed', 'Failed', 'Cancelled'].includes(status)
          ? false
          : 3000;
      },
      staleTime: 0,
    });

  // ── Auto-refresh: WidgetStale (data changed upstream) → re-submit ────────────
  const handleWidgetStale = useCallback(
    (_payload: WidgetStaleEvent) => {
      submit();
    },
    [submit],
  );
  useWidgetSubscription(DASHBOARD_CHANNEL, handleWidgetStale, true);

  const result = pushed ?? polled ?? null;
  const summary = parseKpiGrid(
    result?.status === 'Completed' ? (result.data as unknown) ?? null : null,
  );

  const isLoading = submitting || (!pushed && resultLoading);
  const error =
    submitError ??
    (result?.status === 'Failed' ? result.error ?? 'Request failed' : null) ??
    (resultError instanceof Error ? resultError.message : null);

  return { summary, isLoading, error, submit, requestId };
}
