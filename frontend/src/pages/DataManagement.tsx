import { useState, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getSales,
  addSale,
  updateSale,
  deleteSale,
  getProducts,
  updateProductStock,
  type SaleRecord,
  type ProductRecord,
} from '../api/dataManagement';

// ─── Toast ────────────────────────────────────────────────────────────────────

interface Toast {
  id: number;
  message: string;
  type: 'success' | 'error';
}

function ToastContainer({ toasts, onRemove }: { toasts: Toast[]; onRemove: (id: number) => void }) {
  return (
    <div className="fixed bottom-6 right-6 z-50 flex flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={`flex items-start gap-3 rounded-lg px-4 py-3 shadow-lg text-sm font-medium ${
            t.type === 'success'
              ? 'bg-green-600 text-white'
              : 'bg-red-600 text-white'
          }`}
        >
          <span className="flex-1">{t.message}</span>
          <button onClick={() => onRemove(t.id)} className="ml-2 text-white/80 hover:text-white">
            ✕
          </button>
        </div>
      ))}
    </div>
  );
}

function useToast() {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const counter = useRef(0);

  const addToast = (message: string, type: 'success' | 'error' = 'success') => {
    const id = ++counter.current;
    setToasts((prev) => [...prev, { id, message, type }]);
    setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 4000);
  };

  const removeToast = (id: number) =>
    setToasts((prev) => prev.filter((t) => t.id !== id));

  return { toasts, addToast, removeToast };
}

// ─── Status Badge ─────────────────────────────────────────────────────────────

const STATUS_BADGE: Record<string, string> = {
  ok: 'bg-green-100 text-green-700',
  low: 'bg-yellow-100 text-yellow-700',
  out: 'bg-red-100 text-red-700',
};

const STATUS_LABEL: Record<string, string> = {
  ok: 'Tốt',
  low: 'Thấp',
  out: 'Hết',
};

// ─── Sale Form Modal ──────────────────────────────────────────────────────────

interface SaleFormData {
  date: string;
  region: string;
  product: string;
  category: string;
  revenue: string;
  units: string;
  channel: 'Online' | 'Store';
}

const EMPTY_FORM: SaleFormData = {
  date: '',
  region: '',
  product: '',
  category: '',
  revenue: '',
  units: '',
  channel: 'Online',
};

const REGIONS = ['Bắc', 'Nam', 'Đông', 'Tây', 'Trung'];
const REGION_API_VALUES: Record<string, string> = {
  'Bắc': 'North',
  'Nam': 'South',
  'Đông': 'East',
  'Tây': 'West',
  'Trung': 'Central',
};
const REGION_DISPLAY: Record<string, string> = {
  North: 'Bắc',
  South: 'Nam',
  East: 'Đông',
  West: 'Tây',
  Central: 'Trung',
};

interface SaleModalProps {
  products: ProductRecord[];
  initialData?: SaleRecord | null;
  onClose: () => void;
  onSubmit: (data: Omit<SaleRecord, 'rowIndex'>) => void;
  isLoading: boolean;
}

function SaleModal({ products, initialData, onClose, onSubmit, isLoading }: SaleModalProps) {
  const [form, setForm] = useState<SaleFormData>(() => {
    if (initialData) {
      return {
        date: initialData.date,
        region: REGION_DISPLAY[initialData.region] ?? initialData.region,
        product: initialData.product,
        category: initialData.category,
        revenue: String(initialData.revenue),
        units: String(initialData.units),
        channel: initialData.channel,
      };
    }
    return EMPTY_FORM;
  });

  const [errors, setErrors] = useState<Partial<Record<keyof SaleFormData, string>>>({});

  const handleProductChange = (productName: string) => {
    const prod = products.find((p) => p.name === productName);
    setForm((f) => ({
      ...f,
      product: productName,
      category: prod?.category ?? f.category,
    }));
  };

  const validate = (): boolean => {
    const errs: Partial<Record<keyof SaleFormData, string>> = {};
    if (!form.date) errs.date = 'Vui lòng chọn ngày';
    if (!form.region) errs.region = 'Vui lòng chọn khu vực';
    if (!form.product) errs.product = 'Vui lòng chọn sản phẩm';
    if (!form.category) errs.category = 'Danh mục không được trống';
    if (!form.revenue || Number(form.revenue) <= 0)
      errs.revenue = 'Doanh thu phải lớn hơn 0';
    if (form.units === '' || Number(form.units) < 0)
      errs.units = 'Số lượng phải >= 0';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!validate()) return;
    onSubmit({
      date: form.date,
      region: REGION_API_VALUES[form.region] ?? form.region,
      product: form.product,
      category: form.category,
      revenue: Number(form.revenue),
      units: Number(form.units),
      channel: form.channel,
    });
  };

  const inputCls =
    'w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500';
  const errorCls = 'mt-1 text-xs text-red-600';

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-lg rounded-xl bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h3 className="text-sm font-semibold text-gray-800">
            {initialData ? 'Sửa bản ghi' : 'Thêm bản ghi mới'}
          </h3>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            ✕
          </button>
        </div>
        <form onSubmit={handleSubmit} className="space-y-4 px-6 py-5">
          <div className="grid grid-cols-2 gap-4">
            {/* Date */}
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-700">Ngày</label>
              <input
                type="date"
                value={form.date}
                onChange={(e) => setForm((f) => ({ ...f, date: e.target.value }))}
                className={inputCls}
              />
              {errors.date && <p className={errorCls}>{errors.date}</p>}
            </div>
            {/* Region */}
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-700">Khu vực</label>
              <select
                value={form.region}
                onChange={(e) => setForm((f) => ({ ...f, region: e.target.value }))}
                className={inputCls}
              >
                <option value="">-- Chọn --</option>
                {REGIONS.map((r) => (
                  <option key={r} value={r}>{r}</option>
                ))}
              </select>
              {errors.region && <p className={errorCls}>{errors.region}</p>}
            </div>
            {/* Product */}
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-700">Sản phẩm</label>
              <select
                value={form.product}
                onChange={(e) => handleProductChange(e.target.value)}
                className={inputCls}
              >
                <option value="">-- Chọn --</option>
                {products.map((p) => (
                  <option key={p.productId} value={p.name}>{p.name}</option>
                ))}
              </select>
              {errors.product && <p className={errorCls}>{errors.product}</p>}
            </div>
            {/* Category */}
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-700">Danh mục</label>
              <input
                type="text"
                value={form.category}
                onChange={(e) => setForm((f) => ({ ...f, category: e.target.value }))}
                className={inputCls}
                placeholder="Tự động điền từ sản phẩm"
              />
              {errors.category && <p className={errorCls}>{errors.category}</p>}
            </div>
            {/* Revenue */}
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-700">Doanh thu</label>
              <input
                type="number"
                min={0}
                step="any"
                value={form.revenue}
                onChange={(e) => setForm((f) => ({ ...f, revenue: e.target.value }))}
                className={inputCls}
                placeholder="0"
              />
              {errors.revenue && <p className={errorCls}>{errors.revenue}</p>}
            </div>
            {/* Units */}
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-700">Số lượng</label>
              <input
                type="number"
                min={0}
                value={form.units}
                onChange={(e) => setForm((f) => ({ ...f, units: e.target.value }))}
                className={inputCls}
                placeholder="0"
              />
              {errors.units && <p className={errorCls}>{errors.units}</p>}
            </div>
          </div>
          {/* Channel */}
          <div>
            <label className="mb-2 block text-xs font-medium text-gray-700">Kênh bán</label>
            <div className="flex gap-6">
              {(['Online', 'Store'] as const).map((ch) => (
                <label key={ch} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input
                    type="radio"
                    name="channel"
                    value={ch}
                    checked={form.channel === ch}
                    onChange={() => setForm((f) => ({ ...f, channel: ch }))}
                    className="text-brand-600"
                  />
                  {ch === 'Online' ? 'Trực tuyến' : 'Cửa hàng'}
                </label>
              ))}
            </div>
          </div>
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
            >
              Huỷ
            </button>
            <button
              type="submit"
              disabled={isLoading}
              className="rounded-md bg-brand-600 px-4 py-2 text-sm font-semibold text-white hover:bg-brand-700 disabled:opacity-60"
            >
              {isLoading ? 'Đang lưu…' : 'Lưu'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── Delete Confirm ───────────────────────────────────────────────────────────

function DeleteConfirm({
  onConfirm,
  onCancel,
  isLoading,
}: {
  onConfirm: () => void;
  onCancel: () => void;
  isLoading: boolean;
}) {
  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40">
      <div className="w-80 rounded-xl bg-white p-6 shadow-xl">
        <h3 className="text-sm font-semibold text-gray-800 mb-2">Xác nhận xoá</h3>
        <p className="text-sm text-gray-600 mb-5">Bạn có chắc muốn xoá bản ghi này không? Hành động này không thể hoàn tác.</p>
        <div className="flex justify-end gap-3">
          <button
            onClick={onCancel}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
          >
            Huỷ
          </button>
          <button
            onClick={onConfirm}
            disabled={isLoading}
            className="rounded-md bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-60"
          >
            {isLoading ? 'Đang xoá…' : 'Xoá'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Sales Tab ────────────────────────────────────────────────────────────────

function SalesTab({
  products,
  addToast,
}: {
  products: ProductRecord[];
  addToast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const qc = useQueryClient();
  const [filterDate, setFilterDate] = useState('');
  const [filterRegion, setFilterRegion] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [editRecord, setEditRecord] = useState<SaleRecord | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<number | null>(null);

  const { data: sales = [], isLoading } = useQuery({
    queryKey: ['sales', filterDate, filterRegion],
    queryFn: () =>
      getSales({
        date: filterDate || undefined,
        region: filterRegion || undefined,
      }),
  });

  const addMutation = useMutation({
    mutationFn: (data: Omit<SaleRecord, 'rowIndex'>) => addSale(data),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['sales'] });
      setShowModal(false);
      addToast('Đã thêm bản ghi. Dữ liệu đã cập nhật, biểu đồ sẽ tự refresh.');
    },
    onError: (err: Error) => addToast(`Lỗi: ${err.message}`, 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: ({ rowIndex, data }: { rowIndex: number; data: Partial<SaleRecord> }) =>
      updateSale(rowIndex, data),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['sales'] });
      setEditRecord(null);
      setShowModal(false);
      addToast('Đã cập nhật bản ghi. Dữ liệu đã cập nhật, biểu đồ sẽ tự refresh.');
    },
    onError: (err: Error) => addToast(`Lỗi: ${err.message}`, 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: (rowIndex: number) => deleteSale(rowIndex),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['sales'] });
      setDeleteTarget(null);
      addToast('Đã xoá bản ghi. Dữ liệu đã cập nhật, biểu đồ sẽ tự refresh.');
    },
    onError: (err: Error) => addToast(`Lỗi: ${err.message}`, 'error'),
  });

  const handleSaleSubmit = (data: Omit<SaleRecord, 'rowIndex'>) => {
    if (editRecord) {
      updateMutation.mutate({ rowIndex: editRecord.rowIndex, data });
    } else {
      addMutation.mutate(data);
    }
  };

  const openAdd = () => {
    setEditRecord(null);
    setShowModal(true);
  };

  const openEdit = (record: SaleRecord) => {
    setEditRecord(record);
    setShowModal(true);
  };

  const isMutating = addMutation.isPending || updateMutation.isPending;

  return (
    <>
      {/* Filters */}
      <div className="flex flex-wrap items-end gap-3 mb-4">
        <div>
          <label className="mb-1 block text-xs font-medium text-gray-600">Ngày</label>
          <input
            type="date"
            value={filterDate}
            onChange={(e) => setFilterDate(e.target.value)}
            className="rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none"
          />
        </div>
        <div>
          <label className="mb-1 block text-xs font-medium text-gray-600">Khu vực</label>
          <select
            value={filterRegion}
            onChange={(e) => setFilterRegion(e.target.value)}
            className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-brand-500 focus:outline-none"
          >
            <option value="">Tất cả</option>
            <option value="North">Bắc</option>
            <option value="South">Nam</option>
            <option value="East">Đông</option>
            <option value="West">Tây</option>
            <option value="Central">Trung</option>
          </select>
        </div>
        <button
          onClick={openAdd}
          className="ml-auto rounded-md bg-brand-600 px-4 py-2 text-sm font-semibold text-white hover:bg-brand-700"
        >
          + Thêm bản ghi
        </button>
      </div>

      {/* Table */}
      <div className="overflow-x-auto rounded-lg border border-gray-200">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-gray-50 text-left text-xs font-semibold uppercase text-gray-500">
              <th className="px-4 py-3">Ngày</th>
              <th className="px-4 py-3">Khu vực</th>
              <th className="px-4 py-3">Sản phẩm</th>
              <th className="px-4 py-3">Danh mục</th>
              <th className="px-4 py-3 text-right">Doanh thu</th>
              <th className="px-4 py-3 text-right">SL</th>
              <th className="px-4 py-3">Kênh</th>
              <th className="px-4 py-3">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-gray-400">
                  Đang tải…
                </td>
              </tr>
            ) : sales.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-gray-400">
                  Không có dữ liệu
                </td>
              </tr>
            ) : (
              sales.map((sale) => (
                <tr key={sale.rowIndex} className="border-t border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3 text-gray-700">{sale.date}</td>
                  <td className="px-4 py-3 text-gray-600">
                    {REGION_DISPLAY[sale.region] ?? sale.region}
                  </td>
                  <td className="px-4 py-3 font-medium text-gray-800">{sale.product}</td>
                  <td className="px-4 py-3 text-gray-600">{sale.category}</td>
                  <td className="px-4 py-3 text-right text-gray-700">
                    {sale.revenue.toLocaleString('vi-VN')}
                  </td>
                  <td className="px-4 py-3 text-right text-gray-600">{sale.units}</td>
                  <td className="px-4 py-3">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        sale.channel === 'Online'
                          ? 'bg-blue-100 text-blue-700'
                          : 'bg-purple-100 text-purple-700'
                      }`}
                    >
                      {sale.channel === 'Online' ? 'Trực tuyến' : 'Cửa hàng'}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex gap-2">
                      <button
                        onClick={() => openEdit(sale)}
                        className="rounded px-2 py-1 text-xs font-medium text-brand-600 hover:bg-brand-50"
                      >
                        Sửa
                      </button>
                      <button
                        onClick={() => setDeleteTarget(sale.rowIndex)}
                        className="rounded px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50"
                      >
                        Xoá
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Modals */}
      {showModal && (
        <SaleModal
          products={products}
          initialData={editRecord}
          onClose={() => { setShowModal(false); setEditRecord(null); }}
          onSubmit={handleSaleSubmit}
          isLoading={isMutating}
        />
      )}
      {deleteTarget !== null && (
        <DeleteConfirm
          onConfirm={() => deleteMutation.mutate(deleteTarget)}
          onCancel={() => setDeleteTarget(null)}
          isLoading={deleteMutation.isPending}
        />
      )}
    </>
  );
}

// ─── Products Tab ─────────────────────────────────────────────────────────────

function ProductsTab({
  addToast,
}: {
  addToast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const qc = useQueryClient();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValue, setEditValue] = useState('');

  const { data: products = [], isLoading } = useQuery({
    queryKey: ['products'],
    queryFn: getProducts,
  });

  const stockMutation = useMutation({
    mutationFn: ({ productId, stock }: { productId: string; stock: number }) =>
      updateProductStock(productId, stock),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['products'] });
      setEditingId(null);
      addToast('Đã cập nhật tồn kho. Dữ liệu đã cập nhật, biểu đồ sẽ tự refresh.');
    },
    onError: (err: Error) => addToast(`Lỗi: ${err.message}`, 'error'),
  });

  const startEdit = (p: ProductRecord) => {
    setEditingId(p.productId);
    setEditValue(String(p.currentStock));
  };

  const saveEdit = (productId: string) => {
    const val = Number(editValue);
    if (isNaN(val) || val < 0) {
      addToast('Số lượng không hợp lệ', 'error');
      return;
    }
    stockMutation.mutate({ productId, stock: val });
  };

  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="w-full text-sm">
        <thead>
          <tr className="bg-gray-50 text-left text-xs font-semibold uppercase text-gray-500">
            <th className="px-4 py-3">Tên sản phẩm</th>
            <th className="px-4 py-3">Danh mục</th>
            <th className="px-4 py-3 text-right">Giá</th>
            <th className="px-4 py-3 text-right">Tồn kho</th>
            <th className="px-4 py-3 text-right">Tồn tối thiểu</th>
            <th className="px-4 py-3">Trạng thái</th>
          </tr>
        </thead>
        <tbody>
          {isLoading ? (
            <tr>
              <td colSpan={6} className="px-4 py-8 text-center text-gray-400">
                Đang tải…
              </td>
            </tr>
          ) : products.length === 0 ? (
            <tr>
              <td colSpan={6} className="px-4 py-8 text-center text-gray-400">
                Không có dữ liệu
              </td>
            </tr>
          ) : (
            products.map((p) => (
              <tr key={p.productId} className="border-t border-gray-100 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium text-gray-800">{p.name}</td>
                <td className="px-4 py-3 text-gray-600">{p.category}</td>
                <td className="px-4 py-3 text-right text-gray-700">
                  {p.price.toLocaleString('vi-VN')}
                </td>
                <td className="px-4 py-3 text-right">
                  {editingId === p.productId ? (
                    <input
                      type="number"
                      min={0}
                      value={editValue}
                      onChange={(e) => setEditValue(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') saveEdit(p.productId);
                        if (e.key === 'Escape') setEditingId(null);
                      }}
                      onBlur={() => saveEdit(p.productId)}
                      autoFocus
                      className="w-24 rounded border border-brand-500 px-2 py-1 text-right text-sm focus:outline-none"
                    />
                  ) : (
                    <button
                      onClick={() => startEdit(p)}
                      className="rounded px-2 py-1 font-medium text-gray-700 hover:bg-blue-50 hover:text-brand-700 cursor-pointer"
                      title="Nhấp để chỉnh sửa"
                    >
                      {p.currentStock.toLocaleString()}
                    </button>
                  )}
                </td>
                <td className="px-4 py-3 text-right text-gray-600">
                  {p.minStock.toLocaleString()}
                </td>
                <td className="px-4 py-3">
                  <span
                    className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_BADGE[p.status] ?? ''}`}
                  >
                    {STATUS_LABEL[p.status] ?? p.status}
                  </span>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

// ─── Main Page ────────────────────────────────────────────────────────────────

type Tab = 'sales' | 'products';

export function DataManagement() {
  const [activeTab, setActiveTab] = useState<Tab>('sales');
  const { toasts, addToast, removeToast } = useToast();

  const { data: products = [] } = useQuery({
    queryKey: ['products'],
    queryFn: getProducts,
  });

  const tabCls = (tab: Tab) =>
    `px-4 py-2.5 text-sm font-medium border-b-2 transition-colors ${
      activeTab === tab
        ? 'border-brand-600 text-brand-600'
        : 'border-transparent text-gray-500 hover:text-gray-700'
    }`;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-gray-800">Quản lý dữ liệu</h1>
        <p className="text-sm text-gray-500">
          Thêm, sửa, xoá dữ liệu bán hàng và tồn kho sản phẩm.
        </p>
      </div>

      {/* Tabs */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
        <div className="flex border-b border-gray-200 px-2">
          <button className={tabCls('sales')} onClick={() => setActiveTab('sales')}>
            Dữ liệu bán hàng
          </button>
          <button className={tabCls('products')} onClick={() => setActiveTab('products')}>
            Sản phẩm
          </button>
        </div>
        <div className="p-6">
          {activeTab === 'sales' ? (
            <SalesTab products={products} addToast={addToast} />
          ) : (
            <ProductsTab addToast={addToast} />
          )}
        </div>
      </div>

      <ToastContainer toasts={toasts} onRemove={removeToast} />
    </div>
  );
}
