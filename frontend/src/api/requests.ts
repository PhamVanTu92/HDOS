import { v4 as uuidv4 } from 'uuid';
import { apiGet, apiPost } from './client';
import type {
  RequestEnvelope,
  RequestResult,
  RequestCompletedEvent,
  RequestFailedEvent,
  SubmitAck,
  Priority,
} from '../types/contracts';

// Maps a terminal SignalR push (ResponseDispatchPushMessage) into the same
// RequestResult<T> shape the polling path produces — so consumers can treat push
// and GET identically. Push status wire values: done|failed|timeout|cancelled.
export function mapPushToResult<T = unknown>(
  push: RequestCompletedEvent | RequestFailedEvent,
): RequestResult<T> {
  const base = { requestId: push.requestId, operation: push.operation ?? '' };
  switch ((push.status ?? '').toLowerCase()) {
    case 'done': {
      let data: T | undefined;
      try {
        data = push.payloadJson ? (JSON.parse(push.payloadJson) as T) : undefined;
      } catch {
        data = undefined;
      }
      return { ...base, status: 'Completed', data };
    }
    case 'cancelled':
      return { ...base, status: 'Cancelled' };
    default:
      return {
        ...base,
        status: 'Failed',
        error: push.error?.message ?? `Report ${(push.status ?? 'failed').toLowerCase()}`,
      };
  }
}

export interface SubmitOptions {
  operation: string;
  params: Record<string, unknown>;
  tenantId: string;
  userId: string;
  priority?: Priority;
  cacheSeconds?: number;
  timeoutMs?: number;
}

export async function submitRequest(opts: SubmitOptions): Promise<SubmitAck> {
  const envelope: RequestEnvelope = {
    requestId: uuidv4(),
    operation: opts.operation,
    params: opts.params,
    tenantId: opts.tenantId,
    userId: opts.userId,
    options: {
      priority: opts.priority ?? 'Normal',
      cacheSeconds: opts.cacheSeconds,
      timeoutMs: opts.timeoutMs,
    },
  };
  return apiPost<RequestEnvelope, SubmitAck>('/api/v1/requests', envelope);
}

// Backend response from GET /requests/{id}/result. Shapes:
//  • in_flight (202): { status: "in_flight", requestId, submittedAt }
//  • completed (200): { status: "completed", requestId, result: {
//        status: "Done"|"Failed"|"Cancelled"|"Timeout",
//        payloadJson: string, error: { code, message } | null, ... } }
interface RawResultResponse {
  status: string;
  requestId: string;
  result?: {
    status: string;
    payloadJson: string | null;
    error: { code?: string; message?: string } | null;
  };
}

export async function getRequestResult<T = unknown>(
  requestId: string,
): Promise<RequestResult<T>> {
  const raw = await apiGet<RawResultResponse>(
    `/api/v1/requests/${requestId}/result`,
  );

  // Still processing.
  if (raw.status !== 'completed' || !raw.result) {
    return { requestId, status: 'Processing', operation: '' };
  }

  const inner = raw.result;
  switch (inner.status) {
    case 'Done': {
      let data: T | undefined;
      try {
        data = inner.payloadJson ? (JSON.parse(inner.payloadJson) as T) : undefined;
      } catch {
        data = undefined;
      }
      return { requestId, status: 'Completed', operation: '', data };
    }
    case 'Cancelled':
      return { requestId, status: 'Cancelled', operation: '' };
    case 'Failed':
    case 'Timeout':
    default:
      return {
        requestId,
        status: 'Failed',
        operation: '',
        error: inner.error?.message ?? `Report ${inner.status?.toLowerCase()}`,
      };
  }
}

/** Poll until status is terminal or maxAttempts exceeded. */
export async function pollResult<T = unknown>(
  requestId: string,
  intervalMs = 2000,
  maxAttempts = 60,
): Promise<RequestResult<T>> {
  const terminal = new Set(['Completed', 'Failed', 'Cancelled']);
  for (let i = 0; i < maxAttempts; i++) {
    const result = await getRequestResult<T>(requestId);
    if (terminal.has(result.status)) return result;
    await new Promise((r) => setTimeout(r, intervalMs));
  }
  throw new Error(`Request ${requestId} did not complete within timeout`);
}
