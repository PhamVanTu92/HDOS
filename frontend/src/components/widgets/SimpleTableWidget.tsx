interface TableColumn { key: string; label: string; type?: string; format?: string; align?: string }
interface SimpleTableData {
  columns?: TableColumn[];
  rows?: Record<string, unknown>[];
}

function formatCell(value: unknown, col?: TableColumn): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'number') {
    if (col?.format?.startsWith('currency:')) return value.toLocaleString('vi-VN') + ' ₫';
    if (col?.format?.startsWith('percent:')) return `${value.toFixed(1)}%`;
    return value.toLocaleString('vi-VN');
  }
  return String(value);
}

export function SimpleTableWidget({ data }: { data: unknown }) {
  const d = data as SimpleTableData | null;

  let rows: Record<string, unknown>[] = [];
  let cols: TableColumn[] = [];

  if (d?.rows) {
    rows = d.rows;
    cols = d.columns ?? (rows[0] ? Object.keys(rows[0]).map(k => ({ key: k, label: k })) : []);
  } else if (Array.isArray(data)) {
    rows = data as Record<string, unknown>[];
    cols = rows[0] ? Object.keys(rows[0]).map(k => ({ key: k, label: k })) : [];
  }

  if (!rows.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  return (
    <div className="overflow-auto h-full rounded-lg">
      <table className="w-full text-xs border-collapse">
        <thead className="sticky top-0">
          <tr className="bg-[--overlay]">
            {cols.map(c => (
              <th key={c.key} className="px-3 py-2 text-left font-semibold text-[--tx2] border-b border-[--border] whitespace-nowrap">
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="border-b border-[--border] hover:bg-white/5 transition-colors">
              {cols.map(c => (
                <td key={c.key} className={`px-3 py-2 text-[--tx] whitespace-nowrap ${c.align === 'right' ? 'text-right' : ''}`}>
                  {formatCell(row[c.key], c)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
