import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listProviders,
  registerProvider,
  probeProvider,
  rotateCredentials,
  revokeCredentials,
  type ProviderInfo,
  type ProbeResult,
  type RegisterRequest,
} from '../api/admin';
import { ApiError, hasRealmRole } from '../api/client';

function statusColor(status: ProviderInfo['status']): string {
  switch (status) {
    case 'active':               return 'bg-green-100 text-green-800';
    case 'suspended':            return 'bg-yellow-100 text-yellow-800';
    case 'credentials_revoked':  return 'bg-red-100 text-red-800';
    case 'maintenance':          return 'bg-blue-100 text-blue-800';
    default:                     return 'bg-gray-100 text-gray-800';
  }
}

function statusLabel(status: ProviderInfo['status']): string {
  switch (status) {
    case 'active':               return 'Active';
    case 'suspended':            return 'Suspended';
    case 'credentials_revoked':  return 'Revoked';
    case 'maintenance':          return 'Maintenance';
    default:                     return status;
  }
}

// ── Icons ─────────────────────────────────────────────────────────────────────

function CheckIcon({ className = 'h-4 w-4' }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 13l4 4L19 7" />
    </svg>
  );
}

function XIcon({ className = 'h-4 w-4' }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
    </svg>
  );
}

function SignalIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0" />
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

function RefreshIcon({ spinning = false }: { spinning?: boolean }) {
  return (
    <svg
      className={`h-4 w-4 ${spinning ? 'animate-spin' : ''}`}
      fill="none" stroke="currentColor" viewBox="0 0 24 24"
    >
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
    </svg>
  );
}

// ── Probe Result Display ──────────────────────────────────────────────────────

function ProbeResultPanel({ result }: { result: ProbeResult }) {
  const steps = [
    { label: 'TLS Handshake', ok: result.tlsHandshake },
    { label: 'JWT Accepted',  ok: result.jwtAccepted },
    { label: 'gRPC Welcome',  ok: result.welcomeReceived },
  ];

  const allOk = result.tlsHandshake && result.jwtAccepted && result.welcomeReceived;

  return (
    <div className={`mt-3 rounded-lg border p-3 text-sm ${
      allOk ? 'border-green-200 bg-green-50' : 'border-red-200 bg-red-50'
    }`}>
      <div className="flex items-center justify-between mb-2">
        <span className={`font-semibold ${allOk ? 'text-green-700' : 'text-red-700'}`}>
          {allOk ? 'Kết nối thành công' : 'Kết nối thất bại'}
        </span>
        <span className="text-gray-500">{result.latencyMs} ms</span>
      </div>

      <div className="flex gap-4">
        {steps.map((s) => (
          <div key={s.label} className="flex items-center gap-1">
            <span className={`flex h-5 w-5 items-center justify-center rounded-full ${
              s.ok ? 'bg-green-500 text-white' : 'bg-red-400 text-white'
            }`}>
              {s.ok ? <CheckIcon /> : <XIcon />}
            </span>
            <span className="text-gray-700">{s.label}</span>
          </div>
        ))}
      </div>

      {result.sessionId && (
        <p className="mt-2 text-xs text-gray-500 font-mono truncate">
          Session: {result.sessionId}
        </p>
      )}
      {result.errorDetail && (
        <p className="mt-2 text-xs text-red-600 font-mono">{result.errorDetail}</p>
      )}
    </div>
  );
}

// ── Provider Card ─────────────────────────────────────────────────────────────

function ProviderCard({ provider }: { provider: ProviderInfo }) {
  const queryClient = useQueryClient();
  const [probeResult, setProbeResult] = useState<ProbeResult | null>(null);
  const [showOps, setShowOps] = useState(false);
  const [confirmRevoke, setConfirmRevoke] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);

  const probeMut = useMutation({
    mutationFn: () => probeProvider(provider.providerId),
    onSuccess: (r) => setProbeResult(r),
    onError:   (e) => setFeedback(e instanceof ApiError ? e.message : 'Probe thất bại'),
  });

  const rotateMut = useMutation({
    mutationFn: () => rotateCredentials(provider.providerId),
    onSuccess: (r) => {
      setFeedback(`Đã xoay key lúc ${new Date(r.rotatedAt).toLocaleTimeString()}`);
      void queryClient.invalidateQueries({ queryKey: ['admin-providers'] });
    },
    onError: (e) => setFeedback(e instanceof ApiError ? e.message : 'Xoay key thất bại'),
  });

  const revokeMut = useMutation({
    mutationFn: () => revokeCredentials(provider.providerId),
    onSuccess: () => {
      setConfirmRevoke(false);
      void queryClient.invalidateQueries({ queryKey: ['admin-providers'] });
    },
    onError: (e) => {
      setConfirmRevoke(false);
      setFeedback(e instanceof ApiError ? e.message : 'Revoke thất bại');
    },
  });

  const isRevoked = provider.status === 'credentials_revoked';

  return (
    <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
      {/* Header */}
      <div className="flex items-start justify-between px-5 pt-5 pb-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="font-semibold text-gray-900 truncate">{provider.displayName}</h3>
            <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${statusColor(provider.status)}`}>
              {statusLabel(provider.status)}
            </span>
          </div>
          <p className="mt-0.5 text-xs text-gray-500 font-mono">{provider.providerId}</p>
          {provider.description && (
            <p className="mt-1 text-sm text-gray-600">{provider.description}</p>
          )}
        </div>
        {/* Priority badge */}
        <div className="ml-4 flex-shrink-0 text-center">
          <div className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-gray-100 text-sm font-bold text-gray-700">
            P{provider.priority}
          </div>
          <p className="text-xs text-gray-400 mt-0.5">priority</p>
        </div>
      </div>

      {/* Meta */}
      <div className="px-5 pb-3 flex gap-4 text-xs text-gray-500">
        <span>Client: <code className="text-gray-700">{provider.clientId}</code></span>
        <span>Timeout: {provider.timeoutMs / 1000}s</span>
      </div>

      {/* Operations */}
      <div className="px-5 pb-3">
        <button
          onClick={() => setShowOps(!showOps)}
          className="flex items-center gap-1 text-xs font-medium text-brand-600 hover:text-brand-800"
        >
          <span>{provider.operations.length} operations</span>
          <svg className={`h-3.5 w-3.5 transition-transform ${showOps ? 'rotate-180' : ''}`}
            fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
          </svg>
        </button>
        {showOps && provider.operations.length > 0 && (
          <div className="mt-2 flex flex-wrap gap-1">
            {provider.operations.map((op) => (
              <span key={op}
                className="rounded bg-gray-100 px-2 py-0.5 font-mono text-xs text-gray-700">
                {op}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Probe result */}
      {probeResult && (
        <div className="px-5 pb-3">
          <ProbeResultPanel result={probeResult} />
        </div>
      )}

      {/* Feedback */}
      {feedback && (
        <div className="mx-5 mb-3 rounded bg-blue-50 border border-blue-200 px-3 py-2 text-xs text-blue-700">
          {feedback}
          <button onClick={() => setFeedback(null)} className="ml-2 underline">Đóng</button>
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center gap-2 border-t border-gray-100 px-5 py-3">
        {/* Probe */}
        <button
          onClick={() => { setProbeResult(null); probeMut.mutate(); }}
          disabled={probeMut.isPending}
          className="flex items-center gap-1.5 rounded-md bg-brand-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-60"
        >
          <SignalIcon />
          {probeMut.isPending ? 'Đang probe…' : 'Probe gRPC'}
        </button>

        {/* Rotate */}
        <button
          onClick={() => rotateMut.mutate()}
          disabled={rotateMut.isPending || isRevoked}
          className="flex items-center gap-1.5 rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-40"
        >
          <RefreshIcon spinning={rotateMut.isPending} />
          {rotateMut.isPending ? 'Đang xoay…' : 'Xoay key'}
        </button>

        {/* Revoke */}
        {!isRevoked && (
          confirmRevoke ? (
            <span className="flex items-center gap-2 text-xs">
              <span className="text-red-600 font-medium">Xác nhận thu hồi?</span>
              <button
                onClick={() => revokeMut.mutate()}
                disabled={revokeMut.isPending}
                className="rounded bg-red-600 px-2 py-1 text-white hover:bg-red-700 disabled:opacity-60"
              >
                {revokeMut.isPending ? 'Đang xử lý…' : 'Có, thu hồi'}
              </button>
              <button
                onClick={() => setConfirmRevoke(false)}
                className="rounded border border-gray-300 px-2 py-1 text-gray-600 hover:bg-gray-50"
              >
                Huỷ
              </button>
            </span>
          ) : (
            <button
              onClick={() => setConfirmRevoke(true)}
              className="ml-auto rounded-md border border-red-300 px-3 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50"
            >
              Thu hồi
            </button>
          )
        )}
      </div>
    </div>
  );
}

// ── Register Modal ────────────────────────────────────────────────────────────

interface RegisterModalProps {
  onClose:   () => void;
  onSuccess: () => void;
}

function RegisterModal({ onClose, onSuccess }: RegisterModalProps) {
  const [form, setForm] = useState<RegisterRequest>({
    providerId:   '',
    displayName:  '',
    description:  '',
    clientId:     '',
    clientSecret: '',
    operations:   [],
    timeoutMs:    30000,
    priority:     5,
  });
  const [opsText, setOpsText] = useState('');
  const [error, setError] = useState<string | null>(null);

  const mut = useMutation({
    mutationFn: () => registerProvider({
      ...form,
      operations: opsText
        .split('\n')
        .map((s) => s.trim())
        .filter(Boolean),
    }),
    onSuccess: () => {
      onSuccess();
      onClose();
    },
    onError: (e) => {
      setError(
        e instanceof ApiError
          ? (e.body as { error?: string })?.error ?? e.message
          : 'Đăng ký thất bại',
      );
    },
  });

  function set<K extends keyof RegisterRequest>(key: K, val: RegisterRequest[K]) {
    setForm((f) => ({ ...f, [key]: val }));
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />

      <div className="relative z-10 w-full max-w-lg rounded-xl bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Đăng ký Provider mới</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <XIcon className="h-5 w-5" />
          </button>
        </div>

        <div className="overflow-y-auto max-h-[70vh] px-6 py-5 space-y-4">
          {/* Provider ID */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Provider ID <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              value={form.providerId}
              onChange={(e) => set('providerId', e.target.value)}
              placeholder="excel-provider"
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none font-mono"
            />
          </div>

          {/* Display Name */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Tên hiển thị
            </label>
            <input
              type="text"
              value={form.displayName}
              onChange={(e) => set('displayName', e.target.value)}
              placeholder="Excel Data Provider"
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
            />
          </div>

          {/* Description */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Mô tả
            </label>
            <input
              type="text"
              value={form.description ?? ''}
              onChange={(e) => set('description', e.target.value)}
              placeholder="Mô tả ngắn về provider"
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
            />
          </div>

          {/* Client ID + Secret */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Client ID <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                value={form.clientId}
                onChange={(e) => set('clientId', e.target.value)}
                placeholder="excel-provider"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none font-mono"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Client Secret <span className="text-red-500">*</span>
              </label>
              <input
                type="password"
                value={form.clientSecret}
                onChange={(e) => set('clientSecret', e.target.value)}
                placeholder="••••••••"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>
          </div>

          {/* Operations */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Operations <span className="text-gray-400 font-normal">(mỗi dòng một pattern)</span>
            </label>
            <textarea
              value={opsText}
              onChange={(e) => setOpsText(e.target.value)}
              rows={4}
              placeholder={`report.dashboard.summary\nreport.sales.trend\nreport.inventory.status`}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none font-mono resize-none"
            />
          </div>

          {/* Timeout + Priority */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Timeout (ms)
              </label>
              <input
                type="number"
                value={form.timeoutMs}
                onChange={(e) => set('timeoutMs', parseInt(e.target.value) || 30000)}
                min={1000} max={300000} step={1000}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Priority (1–10)
              </label>
              <input
                type="number"
                value={form.priority}
                onChange={(e) => set('priority', parseInt(e.target.value) || 5)}
                min={1} max={10}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
              />
            </div>
          </div>

          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              {error}
            </div>
          )}
        </div>

        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button
            onClick={onClose}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Huỷ
          </button>
          <button
            onClick={() => mut.mutate()}
            disabled={mut.isPending || !form.providerId || !form.clientId || !form.clientSecret}
            className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-60"
          >
            {mut.isPending ? 'Đang đăng ký…' : 'Đăng ký'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function Admin() {
  const queryClient  = useQueryClient();
  const [showModal, setShowModal] = useState(false);

  const { data: providers, isLoading, error, isRefetching } = useQuery({
    queryKey: ['admin-providers'],
    queryFn:  listProviders,
    refetchInterval: 30_000,
  });

  // Guard: non-admin users
  if (!hasRealmRole('admin')) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-red-100">
            <XIcon className="h-8 w-8 text-red-500" />
          </div>
          <h2 className="text-xl font-semibold text-gray-900">Không có quyền truy cập</h2>
          <p className="mt-2 text-gray-500">Bạn cần vai trò <code>admin</code> để xem trang này.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-5xl mx-auto">
      {/* Page header */}
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Quản trị Provider</h1>
          <p className="mt-1 text-sm text-gray-500">
            Đăng ký, kiểm tra kết nối gRPC và quản lý credentials của các data provider.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <button
            onClick={() => void queryClient.invalidateQueries({ queryKey: ['admin-providers'] })}
            disabled={isRefetching}
            title="Làm mới"
            className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            <RefreshIcon spinning={isRefetching} />
            Làm mới
          </button>
          <button
            onClick={() => setShowModal(true)}
            className="flex items-center gap-1.5 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
          >
            <PlusIcon />
            Đăng ký Provider
          </button>
        </div>
      </div>

      {/* Stats summary */}
      {providers && providers.length > 0 && (
        <div className="mb-6 grid grid-cols-4 gap-4">
          {(
            [
              { label: 'Tổng',          value: providers.length,                               color: 'text-gray-900' },
              { label: 'Active',         value: providers.filter(p => p.status === 'active').length,              color: 'text-green-700' },
              { label: 'Suspended',      value: providers.filter(p => p.status === 'suspended').length,           color: 'text-yellow-700' },
              { label: 'Revoked',        value: providers.filter(p => p.status === 'credentials_revoked').length, color: 'text-red-700' },
            ] as const
          ).map((s) => (
            <div key={s.label} className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm">
              <p className="text-xs font-medium text-gray-500 uppercase tracking-wide">{s.label}</p>
              <p className={`mt-1 text-2xl font-bold ${s.color}`}>{s.value}</p>
            </div>
          ))}
        </div>
      )}

      {/* Loading */}
      {isLoading && (
        <div className="flex items-center justify-center py-20 text-gray-400">
          <RefreshIcon spinning />
          <span className="ml-2">Đang tải danh sách provider…</span>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50 p-6 text-sm text-red-700">
          <strong>Lỗi khi tải danh sách:</strong>{' '}
          {error instanceof Error ? error.message : 'Unknown error'}
        </div>
      )}

      {/* Empty state */}
      {!isLoading && !error && providers?.length === 0 && (
        <div className="flex flex-col items-center justify-center rounded-xl border-2 border-dashed border-gray-300 py-20 text-center">
          <SignalIcon />
          <h3 className="mt-4 text-base font-semibold text-gray-900">Chưa có provider nào</h3>
          <p className="mt-1 text-sm text-gray-500">Bắt đầu bằng cách đăng ký một data provider mới.</p>
          <button
            onClick={() => setShowModal(true)}
            className="mt-4 flex items-center gap-1.5 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
          >
            <PlusIcon />
            Đăng ký Provider
          </button>
        </div>
      )}

      {/* Provider grid */}
      {providers && providers.length > 0 && (
        <div className="grid gap-4 md:grid-cols-1 lg:grid-cols-2">
          {providers.map((p) => (
            <ProviderCard key={p.providerId} provider={p} />
          ))}
        </div>
      )}

      {/* Register Modal */}
      {showModal && (
        <RegisterModal
          onClose={() => setShowModal(false)}
          onSuccess={() => {
            void queryClient.invalidateQueries({ queryKey: ['admin-providers'] });
          }}
        />
      )}
    </div>
  );
}
