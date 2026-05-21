const DATA_API =
  (import.meta.env.VITE_EXCEL_PROVIDER_URL as string | undefined) ??
  'http://localhost:5600';

// ─── Interfaces ───────────────────────────────────────────────────────────────

export interface SaleRecord {
  rowIndex: number;
  date: string;
  region: string;
  product: string;
  category: string;
  revenue: number;
  units: number;
  channel: 'Online' | 'Store';
}

export interface ProductRecord {
  productId: string;
  name: string;
  category: string;
  price: number;
  currentStock: number;
  minStock: number;
  status: 'ok' | 'low' | 'out';
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

async function dataGet<T>(path: string, query?: Record<string, string>): Promise<T> {
  const url = new URL(`${DATA_API}${path}`);
  if (query) {
    Object.entries(query).forEach(([k, v]) => {
      if (v) url.searchParams.set(k, v);
    });
  }
  const res = await fetch(url.toString(), {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`);
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

async function dataPost<TBody, TResponse>(path: string, body: TBody): Promise<TResponse> {
  const res = await fetch(`${DATA_API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`);
  return res.json() as Promise<TResponse>;
}

async function dataPut<TBody, TResponse>(path: string, body: TBody): Promise<TResponse> {
  const res = await fetch(`${DATA_API}${path}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`);
  return res.json() as Promise<TResponse>;
}

async function dataDelete(path: string): Promise<void> {
  const res = await fetch(`${DATA_API}${path}`, {
    method: 'DELETE',
    headers: { 'Content-Type': 'application/json' },
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`);
}

// ─── Sales API ────────────────────────────────────────────────────────────────

export async function getSales(params?: {
  date?: string;
  region?: string;
}): Promise<SaleRecord[]> {
  const query: Record<string, string> = {};
  if (params?.date) query.date = params.date;
  if (params?.region) query.region = params.region;
  return dataGet<SaleRecord[]>('/api/sales', query);
}

export async function addSale(
  data: Omit<SaleRecord, 'rowIndex'>,
): Promise<SaleRecord> {
  return dataPost<Omit<SaleRecord, 'rowIndex'>, SaleRecord>('/api/sales', data);
}

export async function updateSale(
  rowIndex: number,
  data: Partial<SaleRecord>,
): Promise<SaleRecord> {
  return dataPut<Partial<SaleRecord>, SaleRecord>(`/api/sales/${rowIndex}`, data);
}

export async function deleteSale(rowIndex: number): Promise<void> {
  return dataDelete(`/api/sales/${rowIndex}`);
}

// ─── Products API ─────────────────────────────────────────────────────────────

export async function getProducts(): Promise<ProductRecord[]> {
  return dataGet<ProductRecord[]>('/api/products');
}

export async function updateProductStock(
  productId: string,
  stock: number,
): Promise<ProductRecord> {
  return dataPut<{ currentStock: number }, ProductRecord>(
    `/api/products/${encodeURIComponent(productId)}/stock`,
    { currentStock: stock },
  );
}
