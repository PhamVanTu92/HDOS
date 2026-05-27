/**
 * useWidgetData — fetches live data for a module widget.
 *
 * Flow:
 *   1. Resolve paramsTemplate ({{today}} etc.) → concrete params object
 *   2. Submit operation request → get requestId
 *   3. Poll result until terminal OR SSE push arrives
 *   4. Subscribe to SSE WidgetStale — bump refreshKey to refetch
 *
 * Usage:
 *   const { status, data, error, refresh } = useWidgetData(widget, filters);
 */

import { useEffect, useRef, useState, useCallback, useMemo } from 'react';
import { submitRequest, pollResult } from '../api/requests';
import { sseClient } from '../api/sse';
import { getUserClaims } from '../api/client';
import { resolveTemplate, type ResolveContext } from '../utils/templateResolver';
import type { WidgetLayout } from '../types/module';

export type WidgetDataStatus = 'idle' | 'loading' | 'done' | 'error';

export interface WidgetDataResult {
  status:   WidgetDataStatus;
  data:     unknown;
  error:    string | null;
  /** Call to manually trigger a fresh fetch (bypasses cache) */
  refresh:  () => void;
}

export function useWidgetData(
  widget: WidgetLayout | null,
  ctx: ResolveContext = {},
): WidgetDataResult {
  const [status,     setStatus]     = useState<WidgetDataStatus>('idle');
  const [data,       setData]       = useState<unknown>(null);
  const [error,      setError]      = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  const abortRef  = useRef(false);
  const ctxRef    = useRef(ctx);
  ctxRef.current  = ctx; // always current without re-triggering effect

  const refresh = useCallback(() => {
    setRefreshKey(k => k + 1);
  }, []);

  /**
   * Stable signature of only the filter values THIS widget actually references.
   * Pattern: {{filters.xxx}} or {{filters.group.subkey}} in paramsTemplate.
   * Changes only when referenced filter values change → triggers re-fetch.
   * Unrelated filter changes do NOT cause unnecessary re-fetches.
   */
  const filterSignature = useMemo(() => {
    const template = widget?.paramsTemplate;
    if (!template || !ctx.filters) return '';
    // Extract top-level filter keys referenced: {{filters.KEY.optional.sub}}
    const refs = [...template.matchAll(/\{\{filters\.([^}.]+)/g)].map(m => m[1]);
    if (refs.length === 0) return '';
    const unique = [...new Set(refs)];
    return unique.map(k => `${k}=${JSON.stringify(ctx.filters?.[k] ?? null)}`).join('|');
  }, [widget?.paramsTemplate, ctx.filters]);

  // ── Data fetch ──────────────────────────────────────────────────────────────
  useEffect(() => {
    if (!widget?.operationPattern) {
      setStatus('idle');
      return;
    }

    abortRef.current = false;
    setStatus('loading');
    setData(null);
    setError(null);

    const { userId, tenantId } = getUserClaims();

    // Resolve {{token}} placeholders in paramsTemplate
    const params = resolveTemplate(widget.paramsTemplate, {
      ...ctxRef.current,
      userId,
      tenantId,
    });

    void (async () => {
      try {
        const ack = await submitRequest({
          operation:    widget.operationPattern!,
          params,
          tenantId,
          userId,
          cacheSeconds: refreshKey > 0 ? 0 : 60, // bypass cache on manual/SSE refresh
        });

        if (abortRef.current) return;

        // SSE fast path: listen for push while polling
        let resolved = false;
        const unsubPush = sseClient.on('RequestCompleted', (evt) => {
          if (evt.requestId !== ack.requestId || resolved) return;
          resolved = true;
          try {
            const parsed = evt.payloadJson ? JSON.parse(evt.payloadJson) : null;
            setData(parsed);
            setStatus('done');
          } catch {
            setError('Lỗi phân tích dữ liệu từ SSE');
            setStatus('error');
          }
        });

        const unsubFailed = sseClient.on('RequestFailed', (evt) => {
          if (evt.requestId !== ack.requestId || resolved) return;
          resolved = true;
          setError(evt.error?.message ?? 'Thực thi thất bại');
          setStatus('error');
        });

        // Polling fallback
        const result = await pollResult(ack.requestId, 1500, 40);

        // Clean up push listeners
        unsubPush();
        unsubFailed();

        if (abortRef.current || resolved) return;

        if (result.status === 'Completed') {
          setData(result.data ?? null);
          setStatus('done');
        } else {
          setError(result.error ?? 'Thực thi thất bại');
          setStatus('error');
        }
      } catch (e) {
        if (!abortRef.current) {
          setError(e instanceof Error ? e.message : 'Lỗi không xác định');
          setStatus('error');
        }
      }
    })();

    return () => {
      abortRef.current = true;
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [widget?.operationPattern, widget?.paramsTemplate, refreshKey, filterSignature]);

  // ── SSE WidgetStale subscription ────────────────────────────────────────────
  useEffect(() => {
    if (!widget?.operationPattern) return;
    return sseClient.on('WidgetStale', () => {
      // Any stale event for this page triggers a refresh
      // (fine-grained channel filtering can be added later via widget.filterBindings)
      setRefreshKey(k => k + 1);
    });
  }, [widget?.operationPattern]);

  return { status, data, error, refresh };
}
