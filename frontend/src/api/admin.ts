import { apiGet, apiPost } from './client';

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

// ── API calls ─────────────────────────────────────────────────────────────────

export function listProviders(): Promise<ProviderInfo[]> {
  return apiGet<ProviderInfo[]>('/api/v1/admin/providers');
}

export function registerProvider(req: RegisterRequest): Promise<RegisterResult> {
  return apiPost<RegisterRequest, RegisterResult>('/api/v1/admin/providers', req);
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
