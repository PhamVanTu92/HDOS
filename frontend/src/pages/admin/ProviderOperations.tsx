import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listProviders,
  listOperations,
  updateProviderOperations,
  addOperation,
  updateOperation,
  type ProviderInfo,
  type OperationEntry,
  type AddOperationRequest,
  type UpdateOperationRequest,
} from '../../api/admin';
import { ApiError, hasRealmRole } from '../../api/client';

// ── Icons ─────────────────────────────────────────────────────────────────────

function XIcon({ className = 'h-4 w-4' }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
    </svg>
  );
}

function PlusIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
    </svg>
  );
}

function EditIcon() {
  return (
    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
    </svg>
  );
}

function RefreshIcon({ spinning = false }: { spinning?: boolean }) {
  return (
    <svg className={`h-4 w-4 ${spinning ? 'animate-spin' : ''}`}
      fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
    </svg>
  );
}

// ── Provider Operations Editor ────────────────────────────────────────────────

function ProviderOpsCard({ provider }: { provider: ProviderInfo }) {
  const queryClient = useQueryClient();
  const [editing, setEditing]       = useState(false);
  const [ops, setOps]               = useState<string[]>(provider.operations);
  const [newOp, setNewOp]           = useState('');
  const [feedback, setFeedback]     = useState<string | null>(null);

  const saveMut = useMutation({
    mutationFn: () => updateProviderOperations(provider.providerId, ops),
    onSuccess: () => {
      setEditing(false);
      setFeedback('Đã lưu danh sách operations.');
      void queryClient.invalidateQueries({ queryKey: ['admin-providers'] });
      setTimeout(() => setFeedback(null), 3000);
    },
    onError: (e) =>
      setFeedback(e instanceof ApiError ? e.message : 'Lưu thất bại'),
  });

  function startEdit() {
    setOps([...provider.operations]);
    setNewOp('');
    setEditing(true);
    setFeedback(null);
  }

  function cancelEdit() {
    setEditing(false);
    setOps(provider.operations);
    setFeedback(null);
  }

  function removeOp(op: string) {
    setOps((prev) => prev.filter((o) => o !== op));
  }

  function addOp() {
    const v = newOp.trim();
    if (!v || ops.includes(v)) return;
    setOps((prev) => [...prev, v]);
    setNewOp('');
  }

  return (
    <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
      {/* Header */}
      <div className="flex items-center justify-between px-5 pt-4 pb-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-gray-900">{provider.displayName}</h3>
            <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
              provider.status === 'active'
                ? 'bg-green-100 text-green-700'
                : 'bg-gray-100 text-gray-600'
            }`}>{provider.status}</span>
          </div>
          <p className="text-xs text-gray-400 font-mono mt-0.5">{provider.providerId}</p>
        </div>
        {!editing && (
          <button
            onClick={startEdit}
            className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
          >
            <EditIcon />
            Chỉnh sửa
          </button>
        )}
      </div>

      {/* Operations */}
      <div className="px-5 pb-4">
        {editing ? (
          <div className="space-y-3">
            {/* Current ops with remove button */}
            <div className="flex flex-wrap gap-1.5 min-h-8">
              {ops.length === 0 && (
                <span className="text-xs text-gray-400 italic">Chưa có operations</span>
              )}
              {ops.map((op) => (
                <span
                  key={op}
                  className="inline-flex items-center gap-1 rounded bg-brand-50 border border-brand-200 px-2 py-0.5 font-mono text-xs text-brand-800"
                >
                  {op}
                  <button
                    onClick={() => removeOp(op)}
                    className="ml-0.5 text-brand-400 hover:text-red-500"
                  >
                    <XIcon className="h-3 w-3" />
                  </button>
                </span>
              ))}
            </div>

            {/* Add new op */}
            <div className="flex gap-2">
              <input
                type="text"
                value={newOp}
                onChange={(e) => setNewOp(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addOp(); } }}
                placeholder="report.new.operation"
                className="flex-1 rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
              <button
                onClick={addOp}
                disabled={!newOp.trim()}
                className="flex items-center gap-1 rounded-lg bg-brand-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-50"
              >
                <PlusIcon />
                Thêm
              </button>
            </div>

            {/* Save / Cancel */}
            <div className="flex gap-2 pt-1">
              <button
                onClick={() => saveMut.mutate()}
                disabled={saveMut.isPending}
                className="rounded-lg bg-brand-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-60"
              >
                {saveMut.isPending ? 'Đang lưu…' : 'Lưu thay đổi'}
              </button>
              <button
                onClick={cancelEdit}
                className="rounded-lg border border-gray-300 px-4 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
              >
                Huỷ
              </button>
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {provider.operations.length === 0 ? (
              <span className="text-xs text-gray-400 italic">Chưa có operations</span>
            ) : (
              provider.operations.map((op) => (
                <span
                  key={op}
                  className="rounded bg-gray-100 px-2 py-0.5 font-mono text-xs text-gray-700"
                >
                  {op}
                </span>
              ))
            )}
          </div>
        )}

        {feedback && (
          <div className={`mt-2 rounded px-3 py-1.5 text-xs ${
            saveMut.isError
              ? 'bg-red-50 text-red-700 border border-red-200'
              : 'bg-green-50 text-green-700 border border-green-200'
          }`}>
            {feedback}
          </div>
        )}
      </div>
    </div>
  );
}

// ── Operation Registry Tab ────────────────────────────────────────────────────

function OperationRegistryTab() {
  const queryClient = useQueryClient();
  const [showAdd, setShowAdd] = useState(false);
  const [editingEntry, setEditingEntry] = useState<OperationEntry | null>(null);

  const { data: ops, isLoading, error, isRefetching } = useQuery({
    queryKey: ['admin-operations'],
    queryFn:  listOperations,
    retry:    1,
  });

  const addMut = useMutation({
    mutationFn: (req: AddOperationRequest) => addOperation(req),
    onSuccess: () => {
      setShowAdd(false);
      void queryClient.invalidateQueries({ queryKey: ['admin-operations'] });
    },
  });

  const updateMut = useMutation({
    mutationFn: ({ pattern, req }: { pattern: string; req: UpdateOperationRequest }) =>
      updateOperation(pattern, req),
    onSuccess: () => {
      setEditingEntry(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-operations'] });
    },
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12 text-gray-400">
        <RefreshIcon spinning />
        <span className="ml-2">Đang tải operation registry…</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-xl border border-amber-200 bg-amber-50 p-6 text-sm text-amber-700">
        <strong>Không thể tải operation registry.</strong>
        <p className="mt-1 text-xs">
          {error instanceof Error ? error.message : 'Unknown error'} — Backend endpoint
          <code className="mx-1 font-mono">GET /api/v1/admin/operations</code> có thể chưa được triển khai.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-500">{ops?.length ?? 0} entries trong registry</p>
        <div className="flex gap-2">
          <button
            onClick={() => void queryClient.invalidateQueries({ queryKey: ['admin-operations'] })}
            disabled={isRefetching}
            className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            <RefreshIcon spinning={isRefetching} />
            Làm mới
          </button>
          <button
            onClick={() => setShowAdd(true)}
            className="flex items-center gap-1.5 rounded-lg bg-brand-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-brand-700"
          >
            <PlusIcon />
            Thêm operation
          </button>
        </div>
      </div>

      {/* Add form */}
      {showAdd && (
        <AddOperationForm
          onSubmit={(req) => addMut.mutate(req)}
          onCancel={() => setShowAdd(false)}
          isPending={addMut.isPending}
          error={addMut.isError ? (addMut.error instanceof ApiError ? addMut.error.message : 'Thêm thất bại') : null}
        />
      )}

      {/* Table */}
      <div className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm">
        <table className="min-w-full text-sm">
          <thead className="bg-gray-50 text-xs font-semibold uppercase text-gray-500">
            <tr>
              <th className="px-4 py-3 text-left">Operation Pattern</th>
              <th className="px-4 py-3 text-left">Handler / Provider</th>
              <th className="px-4 py-3 text-left">Timeout</th>
              <th className="px-4 py-3 text-left">Cache</th>
              <th className="px-4 py-3 text-left">Status</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {ops?.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-center text-gray-400">
                  Chưa có entries trong registry.
                </td>
              </tr>
            )}
            {ops?.map((entry) =>
              editingEntry?.operationPattern === entry.operationPattern ? (
                <EditOperationRow
                  key={entry.operationPattern}
                  entry={editingEntry}
                  onSave={(req) => updateMut.mutate({ pattern: entry.operationPattern, req })}
                  onCancel={() => setEditingEntry(null)}
                  isPending={updateMut.isPending}
                  error={updateMut.isError ? (updateMut.error instanceof ApiError ? updateMut.error.message : 'Lưu thất bại') : null}
                />
              ) : (
                <tr key={entry.operationPattern} className="hover:bg-gray-50">
                  <td className="px-4 py-3 font-mono text-xs text-gray-800">{entry.operationPattern}</td>
                  <td className="px-4 py-3">
                    <div className="text-xs text-gray-700">{entry.handlerType}</div>
                    {entry.providerId && (
                      <div className="text-xs text-gray-400 font-mono">{entry.providerId}</div>
                    )}
                  </td>
                  <td className="px-4 py-3 text-xs text-gray-600">{entry.timeoutMs / 1000}s</td>
                  <td className="px-4 py-3 text-xs text-gray-600">
                    {entry.cacheable ? `${entry.cacheTtlSeconds}s` : '—'}
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      entry.status === 'active'
                        ? 'bg-green-100 text-green-700'
                        : 'bg-gray-100 text-gray-500'
                    }`}>{entry.status}</span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <button
                      onClick={() => setEditingEntry(entry)}
                      className="text-xs text-brand-600 hover:text-brand-800 font-medium"
                    >
                      Sửa
                    </button>
                  </td>
                </tr>
              )
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Add Operation Form ────────────────────────────────────────────────────────

interface AddOpFormProps {
  onSubmit:  (req: AddOperationRequest) => void;
  onCancel:  () => void;
  isPending: boolean;
  error:     string | null;
}

function AddOperationForm({ onSubmit, onCancel, isPending, error }: AddOpFormProps) {
  const [form, setForm] = useState<AddOperationRequest>({
    operationPattern: '',
    handlerType:      'grpc',
    providerId:       '',
    timeoutMs:        30000,
    cacheable:        true,
    cacheTtlSeconds:  60,
    idempotent:       true,
  });

  function set<K extends keyof AddOperationRequest>(k: K, v: AddOperationRequest[K]) {
    setForm((f) => ({ ...f, [k]: v }));
  }

  return (
    <div className="rounded-xl border border-brand-200 bg-brand-50 p-5 space-y-3">
      <h3 className="font-semibold text-gray-800 text-sm">Thêm operation mới</h3>
      <div className="grid grid-cols-2 gap-3">
        <div className="col-span-2">
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Operation Pattern <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            value={form.operationPattern}
            onChange={(e) => set('operationPattern', e.target.value)}
            placeholder="report.my.operation"
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Handler Type</label>
          <input
            type="text"
            value={form.handlerType ?? ''}
            onChange={(e) => set('handlerType', e.target.value)}
            placeholder="grpc"
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Provider ID</label>
          <input
            type="text"
            value={form.providerId ?? ''}
            onChange={(e) => set('providerId', e.target.value)}
            placeholder="excel-provider"
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Timeout (ms)</label>
          <input
            type="number"
            value={form.timeoutMs}
            onChange={(e) => set('timeoutMs', parseInt(e.target.value) || 30000)}
            min={1000} max={300000} step={1000}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">Cache TTL (giây)</label>
          <input
            type="number"
            value={form.cacheTtlSeconds}
            onChange={(e) => set('cacheTtlSeconds', parseInt(e.target.value) || 60)}
            min={0} max={86400}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
          />
        </div>
        <div className="flex items-center gap-4">
          <label className="flex items-center gap-2 text-xs text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={form.cacheable}
              onChange={(e) => set('cacheable', e.target.checked)}
              className="rounded border-gray-300 text-brand-600"
            />
            Cacheable
          </label>
          <label className="flex items-center gap-2 text-xs text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={form.idempotent}
              onChange={(e) => set('idempotent', e.target.checked)}
              className="rounded border-gray-300 text-brand-600"
            />
            Idempotent
          </label>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</div>
      )}

      <div className="flex gap-2">
        <button
          onClick={() => onSubmit(form)}
          disabled={isPending || !form.operationPattern}
          className="rounded-lg bg-brand-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-60"
        >
          {isPending ? 'Đang thêm…' : 'Thêm'}
        </button>
        <button
          onClick={onCancel}
          className="rounded-lg border border-gray-300 px-4 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
        >
          Huỷ
        </button>
      </div>
    </div>
  );
}

// ── Edit Operation Row ────────────────────────────────────────────────────────

interface EditRowProps {
  entry:     OperationEntry;
  onSave:    (req: UpdateOperationRequest) => void;
  onCancel:  () => void;
  isPending: boolean;
  error:     string | null;
}

function EditOperationRow({ entry, onSave, onCancel, isPending, error }: EditRowProps) {
  const [form, setForm] = useState<UpdateOperationRequest>({
    handlerType:     entry.handlerType,
    providerId:      entry.providerId ?? '',
    timeoutMs:       entry.timeoutMs,
    cacheable:       entry.cacheable,
    cacheTtlSeconds: entry.cacheTtlSeconds,
    idempotent:      entry.idempotent,
    status:          entry.status,
  });

  function set<K extends keyof UpdateOperationRequest>(k: K, v: UpdateOperationRequest[K]) {
    setForm((f) => ({ ...f, [k]: v }));
  }

  return (
    <tr className="bg-brand-50">
      <td className="px-4 py-3 font-mono text-xs text-gray-800">{entry.operationPattern}</td>
      <td className="px-4 py-3">
        <input
          type="text"
          value={form.handlerType ?? ''}
          onChange={(e) => set('handlerType', e.target.value)}
          className="w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono focus:border-brand-500 outline-none"
          placeholder="grpc"
        />
        <input
          type="text"
          value={form.providerId ?? ''}
          onChange={(e) => set('providerId', e.target.value)}
          className="mt-1 w-full rounded border border-gray-300 px-2 py-1 text-xs font-mono focus:border-brand-500 outline-none"
          placeholder="provider-id"
        />
      </td>
      <td className="px-4 py-3">
        <input
          type="number"
          value={form.timeoutMs}
          onChange={(e) => set('timeoutMs', parseInt(e.target.value) || 30000)}
          className="w-24 rounded border border-gray-300 px-2 py-1 text-xs focus:border-brand-500 outline-none"
        />
      </td>
      <td className="px-4 py-3">
        <input
          type="number"
          value={form.cacheTtlSeconds}
          onChange={(e) => set('cacheTtlSeconds', parseInt(e.target.value) || 60)}
          disabled={!form.cacheable}
          className="w-20 rounded border border-gray-300 px-2 py-1 text-xs focus:border-brand-500 outline-none disabled:opacity-50"
        />
      </td>
      <td className="px-4 py-3">
        <select
          value={form.status}
          onChange={(e) => set('status', e.target.value as 'active' | 'disabled')}
          className="rounded border border-gray-300 px-2 py-1 text-xs focus:border-brand-500 outline-none"
        >
          <option value="active">active</option>
          <option value="disabled">disabled</option>
        </select>
      </td>
      <td className="px-4 py-3">
        <div className="flex flex-col gap-1">
          {error && <p className="text-xs text-red-600">{error}</p>}
          <div className="flex gap-1">
            <button
              onClick={() => onSave(form)}
              disabled={isPending}
              className="rounded bg-brand-600 px-2 py-1 text-xs text-white hover:bg-brand-700 disabled:opacity-60"
            >
              {isPending ? '…' : 'Lưu'}
            </button>
            <button
              onClick={onCancel}
              className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-600 hover:bg-gray-50"
            >
              Huỷ
            </button>
          </div>
        </div>
      </td>
    </tr>
  );
}

// ── Provider Operations Tab ───────────────────────────────────────────────────

function ProviderOperationsTab() {
  const queryClient = useQueryClient();
  const { data: providers, isLoading, error, isRefetching } = useQuery({
    queryKey:        ['admin-providers'],
    queryFn:         listProviders,
    refetchInterval: 60_000,
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-500">
          Chỉnh sửa danh sách operations của từng provider.
        </p>
        <button
          onClick={() => void queryClient.invalidateQueries({ queryKey: ['admin-providers'] })}
          disabled={isRefetching}
          className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          <RefreshIcon spinning={isRefetching} />
          Làm mới
        </button>
      </div>

      {isLoading && (
        <div className="flex items-center justify-center py-12 text-gray-400">
          <RefreshIcon spinning />
          <span className="ml-2">Đang tải…</span>
        </div>
      )}

      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error instanceof Error ? error.message : 'Lỗi khi tải danh sách provider'}
        </div>
      )}

      {providers?.length === 0 && !isLoading && (
        <div className="rounded-xl border-2 border-dashed border-gray-200 p-8 text-center text-gray-400">
          Chưa có provider nào được đăng ký.
        </div>
      )}

      <div className="space-y-3">
        {providers?.map((p) => (
          <ProviderOpsCard key={p.providerId} provider={p} />
        ))}
      </div>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function ProviderOperations() {
  const [activeTab, setActiveTab] = useState<'providers' | 'registry'>('providers');

  if (!hasRealmRole('admin')) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-red-100">
            <XIcon className="h-8 w-8 text-red-500" />
          </div>
          <h2 className="text-xl font-semibold text-gray-900">Không có quyền truy cập</h2>
          <p className="mt-2 text-gray-500">
            Bạn cần vai trò <code>admin</code> để xem trang này.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-5xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Quản lý Operations</h1>
        <p className="mt-1 text-sm text-gray-500">
          Chỉnh sửa operations của từng provider và quản lý operation registry.
        </p>
      </div>

      {/* Tabs */}
      <div className="mb-6 flex gap-1 rounded-xl border border-gray-200 bg-gray-50 p-1 w-fit">
        {(
          [
            { id: 'providers', label: 'Operations theo Provider' },
            { id: 'registry', label: 'Operation Registry' },
          ] as const
        ).map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`rounded-lg px-4 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.id
                ? 'bg-white text-gray-900 shadow-sm'
                : 'text-gray-500 hover:text-gray-700'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'providers' && <ProviderOperationsTab />}
      {activeTab === 'registry' && <OperationRegistryTab />}
    </div>
  );
}
