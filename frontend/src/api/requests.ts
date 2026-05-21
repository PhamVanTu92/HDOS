import { v4 as uuidv4 } from 'uuid';
import { apiGet, apiPost } from './client';
import type {
  RequestEnvelope,
  RequestResult,
  SubmitAck,
  Priority,
} from '../types/contracts';

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

export async function getRequestResult<T = unknown>(
  requestId: string,
): Promise<RequestResult<T>> {
  return apiGet<RequestResult<T>>(`/api/v1/requests/${requestId}/result`);
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
