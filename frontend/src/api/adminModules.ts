/**
 * Admin Module API — wraps all /api/v1/admin/modules/* endpoints.
 */

import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { AdminModule, UpsertModuleRequest, UpsertWidgetRequest, WidgetTypeCatalogEntry } from '../types/module';

// ── Module Groups ─────────────────────────────────────────────────────────────

export interface ModuleGroup {
  id:        string;
  slug:      string;
  label:     string;
  icon:      string | null;
  sortOrder: number;
}

export function listModuleGroups(): Promise<ModuleGroup[]> {
  return apiGet('/api/v1/admin/module-groups');
}

// ── Modules ───────────────────────────────────────────────────────────────────

export function listAdminModules(): Promise<AdminModule[]> {
  return apiGet('/api/v1/admin/modules');
}

export function createModule(req: UpsertModuleRequest): Promise<{ id: string; slug: string; label: string }> {
  return apiPost('/api/v1/admin/modules', req);
}

export function updateModule(slug: string, req: Partial<UpsertModuleRequest>): Promise<{ id: string; slug: string }> {
  return apiPut(`/api/v1/admin/modules/${slug}`, req);
}

export function deleteModule(slug: string): Promise<void> {
  return apiDelete(`/api/v1/admin/modules/${slug}`);
}

// ── Tabs ──────────────────────────────────────────────────────────────────────

export interface AdminTab {
  id:          string;
  slug:        string;
  label:       string;
  sortOrder:   number;
  isDefault:   boolean;
  widgetCount: number;
}

export interface CreateTabRequest {
  slug:      string;
  label:     string;
  sortOrder?: number;
  isDefault?: boolean;
}

export function listTabs(moduleSlug: string): Promise<AdminTab[]> {
  return apiGet(`/api/v1/admin/modules/${moduleSlug}/tabs`);
}

export function createTab(moduleSlug: string, req: CreateTabRequest): Promise<{ id: string; slug: string; label: string }> {
  return apiPost(`/api/v1/admin/modules/${moduleSlug}/tabs`, req);
}

export interface UpdateTabRequest {
  label?:     string;
  sortOrder?: number;
  isDefault?: boolean;
}

export function updateTab(
  moduleSlug: string,
  tabId: string,
  req: UpdateTabRequest,
): Promise<{ id: string }> {
  return apiPut(`/api/v1/admin/modules/${moduleSlug}/tabs/${tabId}`, req);
}

export function deleteTab(moduleSlug: string, tabId: string): Promise<void> {
  return apiDelete(`/api/v1/admin/modules/${moduleSlug}/tabs/${tabId}`);
}

// ── Widget Canvas ─────────────────────────────────────────────────────────────

export interface SaveWidgetsResponse {
  saved: number;
}

export function saveWidgets(
  moduleSlug: string,
  tabId:      string,
  widgets:    UpsertWidgetRequest[],
): Promise<SaveWidgetsResponse> {
  return apiPut(`/api/v1/admin/modules/${moduleSlug}/tabs/${tabId}/widgets`, widgets);
}

// ── Widget Type Catalog ───────────────────────────────────────────────────────

export function listWidgetSchemas(category?: string): Promise<WidgetTypeCatalogEntry[]> {
  const qs = category ? `?category=${encodeURIComponent(category)}` : '';
  return apiGet(`/api/v1/admin/schemas${qs}`);
}
