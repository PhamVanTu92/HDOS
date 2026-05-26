import { apiGet, apiPost, apiPut, apiDelete } from './client';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface ProviderInfo {
  providerId:   string;
  displayName:  string;
  description:  string | null;
  clientId:     string;
  operations:   string[];
  timeoutMs:    number;
  priority:     number;
  status:       'active' | 'suspended' | 'credentials_revoked' | 'maintenance';
  createdAt:    string;
  updatedAt:    string;
}

export interface ProbeResult {
  tlsHandshake:    boolean;
  jwtAccepted:     boolean;
  welcomeReceived: boolean;
  latencyMs:       number;
  sessionId:       string | null;
  errorDetail:     string | null;
}

export interface RotateResult {
  providerId: string;
  rotatedAt:  string;
  newSecret:  string;   // plaintext — hiển thị một lần duy nhất
}

export interface RegisterRequest {
  providerId:   string;
  displayName:  string;
  description?: string;
  clientId:     string;
  clientSecret: string;
  operations:   string[];
  timeoutMs:    number;
  priority:     number;
}

export interface RegisterResult {
  providerId:    string;
  registeredAt:  string;
}

export interface UpdateProviderRequest {
  displayName?: string;
  description?: string;
  operations?:  string[];
  timeoutMs?:   number;
  priority?:    number;
  status?:      string;
}

// ── API calls ─────────────────────────────────────────────────────────────────

export function listProviders(): Promise<ProviderInfo[]> {
  return apiGet<ProviderInfo[]>('/api/v1/admin/providers');
}

export function registerProvider(req: RegisterRequest): Promise<RegisterResult> {
  return apiPost<RegisterRequest, RegisterResult>('/api/v1/admin/providers', req);
}

export function updateProvider(providerId: string, req: UpdateProviderRequest): Promise<ProviderInfo> {
  return apiPut<UpdateProviderRequest, ProviderInfo>(`/api/v1/admin/providers/${providerId}`, req);
}

export function probeProvider(providerId: string): Promise<ProbeResult> {
  return apiPost<Record<string, never>, ProbeResult>(
    `/api/v1/admin/providers/${providerId}/probe`,
    {},
  );
}

export function rotateCredentials(providerId: string): Promise<RotateResult> {
  return apiPost<Record<string, never>, RotateResult>(
    `/api/v1/admin/providers/${providerId}/credentials/rotate`,
    {},
  );
}

export function revokeCredentials(providerId: string): Promise<void> {
  return apiPost<Record<string, never>, void>(
    `/api/v1/admin/providers/${providerId}/credentials/revoke`,
    {},
  );
}

// ── Operation Registry ────────────────────────────────────────────────────────

export interface OperationEntry {
  id:               number;
  operationPattern: string;
  handlerType:      string;
  providerId:       string | null;
  paramsSchema:     string | null;   // raw JSON text
  timeoutMs:        number;
  cacheable:        boolean;
  cacheTtlSeconds:  number | null;
  idempotent:       boolean;
  status:           'active' | 'deprecated' | 'disabled';
  createdAt:        string;
  updatedAt:        string;
}

export interface AddOperationRequest {
  operationPattern: string;
  handlerType:      string;
  providerId:       string | null;
  paramsSchemaJson: string | null;  // raw JSON string
  timeoutMs:        number;
  cacheable:        boolean;
  cacheTtlSeconds:  number | null;
  idempotent:       boolean;
}

export interface UpdateOperationRequest {
  handlerType:      string;
  providerId:       string | null;
  paramsSchemaJson: string | null;
  timeoutMs:        number;
  cacheable:        boolean;
  cacheTtlSeconds:  number | null;
  idempotent:       boolean;
  status:           string;
}

export function listOperations(): Promise<OperationEntry[]> {
  return apiGet<OperationEntry[]>('/api/v1/admin/operations');
}

export function addOperation(req: AddOperationRequest): Promise<OperationEntry> {
  return apiPost<AddOperationRequest, OperationEntry>('/api/v1/admin/operations', req);
}

export function updateOperation(
  pattern: string,
  req: UpdateOperationRequest,
): Promise<OperationEntry> {
  return apiPut<UpdateOperationRequest, OperationEntry>(
    `/api/v1/admin/operations/${encodeURIComponent(pattern)}`,
    req,
  );
}

export function deleteOperation(pattern: string): Promise<void> {
  return apiDelete(`/api/v1/admin/operations/${encodeURIComponent(pattern)}`);
}
