/**
 * MenuManager — Quản lý menu báo cáo (admin)
 * Route: /admin/menus
 *
 * Chức năng:
 *  - Tree menu bên trái, load từ API
 *  - Tab Màn hình: CRUD screens + mở Screen Designer
 *  - Tab Phân quyền: add/remove role/user permissions
 *  - Tab Thông tin: sửa tên, slug, icon, vị trí, hiển thị
 *  - Screen Designer: drag-and-drop widget từ palette, resize, cấu hình data source
 */

import { useState, useEffect, useRef, useCallback } from 'react';
import { apiGet, apiPost, apiPut, apiDelete } from '../../api/client';
import type {
  AdminMenuNode, AdminScreen, AdminPermission, WidgetDef,
} from '../../types/menuTypes';

// ── Data source registry ───────────────────────────────────────────────────────

interface DsField { k: string; t: 'num' | 'str' | 'date'; l: string }
interface DsInfo {
  label: string; type: 'SQL' | 'Excel' | 'gRPC'; provider: string;
  fields: DsField[]; compat: string[];
  preview: Record<string, unknown>;
}

const DS: Record<string, DsInfo> = {
  'sql.sales_monthly': {
    label:'Doanh thu theo tháng', type:'SQL', provider:'request-api',
    fields:[{k:'month',t:'date',l:'Tháng'},{k:'revenue',t:'num',l:'Doanh thu (triệu)'},{k:'target',t:'num',l:'Mục tiêu'},{k:'growth_pct',t:'num',l:'Tăng trưởng %'}],
    compat:['line','bar'], preview:{lbs:['T1','T2','T3','T4','T5','T6','T7','T8','T9','T10','T11','T12'],vals:[320,410,380,450,420,510,480,560,530,610,580,650]},
  },
  'sql.top_products': {
    label:'Top sản phẩm', type:'SQL', provider:'request-api',
    fields:[{k:'product_name',t:'str',l:'Sản phẩm'},{k:'revenue',t:'num',l:'Doanh thu'},{k:'quantity',t:'num',l:'Số lượng'},{k:'category',t:'str',l:'Danh mục'}],
    compat:['bar','table','pie'], preview:{rows:[['SP Alu 6061',920],['SP Thép CT3',780],['SP Nhựa PP',650],['SP Inox 304',540],['SP Đồng ĐC',410]]},
  },
  'sql.kpi_summary': {
    label:'KPI tổng hợp', type:'SQL', provider:'request-api',
    fields:[{k:'total_revenue',t:'num',l:'Tổng doanh thu'},{k:'growth_pct',t:'num',l:'Tăng trưởng %'},{k:'order_count',t:'num',l:'Số đơn hàng'},{k:'avg_order',t:'num',l:'Giá trị TB/đơn'}],
    compat:['kpi'], preview:{val:'2.47 tỷ', trend:'+18.4%', lbl:'Tổng doanh thu 2025'},
  },
  'sql.orders_recent': {
    label:'Đơn hàng gần đây', type:'SQL', provider:'request-api',
    fields:[{k:'order_id',t:'str',l:'Mã đơn'},{k:'customer',t:'str',l:'Khách hàng'},{k:'product',t:'str',l:'Sản phẩm'},{k:'amount',t:'num',l:'Giá trị'},{k:'date',t:'date',l:'Ngày'},{k:'status',t:'str',l:'Trạng thái'}],
    compat:['table'], preview:{cols:['Mã đơn','Khách hàng','Giá trị','Trạng thái'],rows:[['ORD-001','Cty ABC','120M','✅'],['ORD-002','Cty XYZ','85M','🚚'],['ORD-003','DN 123','64M','✅']]},
  },
  'sql.regional_stats': {
    label:'Thống kê vùng', type:'SQL', provider:'request-api',
    fields:[{k:'region',t:'str',l:'Vùng'},{k:'revenue',t:'num',l:'Doanh thu'},{k:'orders',t:'num',l:'Đơn hàng'},{k:'customers',t:'num',l:'Khách hàng'}],
    compat:['bar','table','pie'], preview:{rows:[['Miền Bắc',820],['Miền Trung',450],['Miền Nam',930],['Tây Nguyên',210]]},
  },
  'excel.sales_dashboard': {
    label:'Excel: Sales Dashboard', type:'Excel', provider:'excel-provider',
    fields:[{k:'month',t:'date',l:'Tháng'},{k:'revenue',t:'num',l:'Doanh thu'},{k:'product',t:'str',l:'Sản phẩm'},{k:'quantity',t:'num',l:'Số lượng'},{k:'region',t:'str',l:'Vùng'}],
    compat:['line','bar','table','kpi','pie'], preview:{lbs:['T1','T2','T3','T4','T5','T6'],vals:[280,350,310,420,390,480]},
  },
  'ml.fraud.score': {
    label:'ML: Fraud Score', type:'gRPC', provider:'dotnet-provider',
    fields:[{k:'transactionId',t:'str',l:'Transaction ID'},{k:'score',t:'num',l:'Fraud Score'},{k:'riskBand',t:'str',l:'Mức độ rủi ro'},{k:'modelVersion',t:'str',l:'Model version'}],
    compat:['kpi','table'], preview:{val:'0.234', trend:'LOW RISK', lbl:'Fraud Score trung bình'},
  },
  'ml.fraud.batchScore': {
    label:'ML: Batch Fraud Score', type:'gRPC', provider:'dotnet-provider',
    fields:[{k:'transactionId',t:'str',l:'Transaction ID'},{k:'score',t:'num',l:'Fraud Score'},{k:'riskBand',t:'str',l:'Rủi ro'}],
    compat:['bar','table'], preview:{rows:[['TXN-001',0.12,'LOW'],['TXN-002',0.67,'HIGH'],['TXN-003',0.34,'MED']]},
  },
};

const ICONS = ['📊','📈','📉','📋','📌','🗃️','⚙️','✅','🛡️','🏭','💼','🎯','🔍','📁','🌐','⭐'];
const COLORS = ['#4f46e5','#0ea5e9','#10b981','#f59e0b','#ef4444','#8b5cf6','#06b6d4','#f97316'];
const ROLE_OPTIONS = ['admin','manager','operator','viewer','user'];

// ── Types ──────────────────────────────────────────────────────────────────────

interface DesignerWidget {
  id: string; type: string; title: string; span: number; color: string;
  ds: string; xField?: string; yField?: string; valField?: string;
  trendField?: string; catField?: string; cols?: string[];
}

// ── Helpers ────────────────────────────────────────────────────────────────────

const uid = () => 'w' + Date.now() + Math.random().toString(36).slice(2, 5);
const slugify = (s: string) =>
  s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
   .replace(/đ/g, 'd').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const dsCls = (t: string) => t === 'SQL' ? 'bg-sky-100 text-sky-700' : t === 'Excel' ? 'bg-green-100 text-green-700' : 'bg-purple-100 text-purple-700';
const ftCls = (t: string) => t === 'num' ? 'bg-sky-100 text-sky-700' : t === 'date' ? 'bg-green-100 text-green-700' : 'bg-purple-100 text-purple-700';
const defaultSpan = (t: string) => ({ kpi:4, line:8, bar:6, pie:4, table:12, text:6 }[t] ?? 6);
const defaultTitle = (t: string) => ({ kpi:'KPI Card', line:'Line Chart', bar:'Bar Chart', pie:'Pie Chart', table:'Bảng dữ liệu', text:'Văn bản' }[t] ?? 'Widget');

// ── WidgetPreview ──────────────────────────────────────────────────────────────

function WidgetPreview({ wg }: { wg: DesignerWidget }) {
  const ds = DS[wg.ds];
  switch (wg.type) {
    case 'kpi': {
      const p = (ds?.preview ?? {}) as { val?: string; trend?: string; lbl?: string };
      return (
        <div>
          <div className="text-2xl font-black" style={{ color: wg.color }}>{p.val ?? '—'}</div>
          <div className="text-xs text-gray-400 mt-0.5">{p.lbl ?? wg.title}</div>
          {p.trend && <div className="text-xs mt-1" style={{ color: wg.color }}>▲ {p.trend}</div>}
        </div>
      );
    }
    case 'line': {
      const p = (ds?.preview ?? {}) as { vals?: number[] };
      const vals = p.vals ?? [40,60,50,80,70,90,85,100];
      const mx = Math.max(...vals), mn = Math.min(...vals);
      const pts = vals.map((v,i) => `${(i/(vals.length-1))*290+5},${65-((v-mn)/(mx-mn||1))*55}`).join(' ');
      return (
        <svg viewBox="0 0 300 70" className="w-full" style={{height:65}} preserveAspectRatio="none">
          <defs><linearGradient id={`lg${wg.id}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={wg.color} stopOpacity=".3"/>
            <stop offset="100%" stopColor={wg.color} stopOpacity="0"/>
          </linearGradient></defs>
          <polyline points={pts} fill="none" stroke={wg.color} strokeWidth="2"/>
          <polygon points={`${pts} 295,70 5,70`} fill={`url(#lg${wg.id})`}/>
        </svg>
      );
    }
    case 'bar': {
      const p = (ds?.preview ?? {}) as { rows?: [string, number][] };
      const rows = (p.rows ?? [['A',80],['B',65],['C',50]]).slice(0,4) as [string,number][];
      const mx = Math.max(...rows.map(r => r[1]));
      return (
        <div className="flex flex-col gap-1 mt-1">
          {rows.map(([l, v]) => (
            <div key={l} className="flex items-center gap-1.5 text-[10px]">
              <span className="w-16 text-right text-gray-400 truncate">{l}</span>
              <div className="flex-1 bg-gray-100 rounded h-2">
                <div className="h-full rounded" style={{ width: `${v/mx*100}%`, background: wg.color }} />
              </div>
              <span className="w-6 text-gray-500">{v}</span>
            </div>
          ))}
        </div>
      );
    }
    case 'pie': {
      const p = (ds?.preview ?? {}) as { rows?: [string, number][] };
      const rows = (p.rows ?? [['A',40],['B',30],['C',20],['D',10]]).slice(0,4) as [string,number][];
      const total = rows.reduce((s,r) => s+r[1], 0);
      let off = 0;
      const segs = rows.map(([,v],i) => {
        const d = v/total*283; const seg = <circle key={i} cx="60" cy="60" r="45" fill="none" stroke={COLORS[i]} strokeWidth="20" strokeDasharray={`${d} ${283-d}`} strokeDashoffset={-off} transform="rotate(-90 60 60)"/>;
        off += d; return seg;
      });
      return (
        <svg viewBox="0 0 120 120" className="w-full" style={{maxHeight:80}}>
          {segs}<circle cx="60" cy="60" r="30" fill="white"/>
          <text x="60" y="65" textAnchor="middle" fill="#374151" fontSize="9" fontWeight="700">{rows[0][0]}: {(rows[0][1]/total*100).toFixed(0)}%</text>
        </svg>
      );
    }
    case 'table': {
      const p = (ds?.preview ?? {}) as { cols?: string[]; rows?: unknown[][] };
      const cols = p.cols ?? ['Col 1','Col 2','Col 3'];
      const rows = (p.rows ?? [['—','—','—'],['—','—','—']]).slice(0,3) as unknown[][];
      return (
        <table className="w-full text-[10px]">
          <thead><tr>{cols.map(c => <th key={c} className="text-left px-1 py-0.5 text-gray-400 border-b border-gray-100">{c}</th>)}</tr></thead>
          <tbody>{rows.map((r,i) => <tr key={i}>{(r as string[]).map((c,j) => <td key={j} className="px-1 py-0.5 text-gray-500">{String(c)}</td>)}</tr>)}</tbody>
        </table>
      );
    }
    case 'text':
      return <p className="text-xs text-gray-400 italic">Văn bản tự do — tiêu đề, chú thích…</p>;
    default: return null;
  }
}

// ── ConfigPanel ────────────────────────────────────────────────────────────────

function ConfigPanel({
  wg, onUpdate, onDelete,
}: {
  wg: DesignerWidget;
  onUpdate: (patch: Partial<DesignerWidget>) => void;
  onDelete: () => void;
}) {
  const ds = DS[wg.ds];
  const numFields = ds?.fields.filter(f => f.t === 'num') ?? [];
  const allFields = ds?.fields ?? [];

  return (
    <div className="flex flex-col h-full">
      <div className="p-3 border-b border-gray-100 text-xs font-semibold text-gray-500">⚙️ Cấu hình widget</div>
      <div className="flex-1 overflow-y-auto p-3 space-y-3">
        {/* Title */}
        <div>
          <label className="text-[10px] text-gray-400 font-medium block mb-1">Tiêu đề</label>
          <input value={wg.title} onChange={e => onUpdate({ title: e.target.value })}
            className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 focus:border-brand-400 focus:outline-none"/>
        </div>

        {/* Data source */}
        <div>
          <label className="text-[10px] text-gray-400 font-medium block mb-1">Nguồn dữ liệu</label>
          {ds && <span className={`inline-block text-[9px] rounded px-1.5 py-0.5 font-semibold mb-1 ${dsCls(ds.type)}`}>{ds.type} · {ds.provider}</span>}
          <select value={wg.ds} onChange={e => onUpdate({ ds: e.target.value, xField:'', yField:'', valField:'', trendField:'', catField:'', cols:[] })}
            className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 focus:border-brand-400 focus:outline-none bg-white">
            <option value="">-- Chọn data source --</option>
            <optgroup label="SQL Provider">
              {['sql.sales_monthly','sql.top_products','sql.kpi_summary','sql.orders_recent','sql.regional_stats'].map(k => <option key={k} value={k}>{DS[k].label}</option>)}
            </optgroup>
            <optgroup label="Excel Provider"><option value="excel.sales_dashboard">{DS['excel.sales_dashboard'].label}</option></optgroup>
            <optgroup label="ML Provider (gRPC)">
              {['ml.fraud.score','ml.fraud.batchScore'].map(k => <option key={k} value={k}>{DS[k].label}</option>)}
            </optgroup>
          </select>
        </div>

        {/* Field config by type */}
        {ds && (wg.type === 'kpi') && (
          <>
            <div>
              <label className="text-[10px] text-gray-400 font-medium block mb-1">Giá trị chính</label>
              <select value={wg.valField ?? ''} onChange={e => onUpdate({ valField: e.target.value })}
                className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 bg-white focus:border-brand-400 focus:outline-none">
                <option value="">-- chọn field --</option>
                {numFields.map(f => <option key={f.k} value={f.k}>{f.l}</option>)}
              </select>
            </div>
            <div>
              <label className="text-[10px] text-gray-400 font-medium block mb-1">Xu hướng</label>
              <select value={wg.trendField ?? ''} onChange={e => onUpdate({ trendField: e.target.value })}
                className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 bg-white focus:border-brand-400 focus:outline-none">
                <option value="">-- chọn field --</option>
                {allFields.map(f => <option key={f.k} value={f.k}>{f.l}</option>)}
              </select>
            </div>
          </>
        )}
        {ds && (wg.type === 'line' || wg.type === 'bar') && (
          <>
            <div>
              <label className="text-[10px] text-gray-400 font-medium block mb-1">Trục X (danh mục)</label>
              <select value={wg.xField ?? ''} onChange={e => onUpdate({ xField: e.target.value })}
                className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 bg-white focus:border-brand-400 focus:outline-none">
                <option value="">-- chọn field --</option>
                {allFields.map(f => <option key={f.k} value={f.k}>{f.l}</option>)}
              </select>
            </div>
            <div>
              <label className="text-[10px] text-gray-400 font-medium block mb-1">Trục Y (giá trị)</label>
              <select value={wg.yField ?? ''} onChange={e => onUpdate({ yField: e.target.value })}
                className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 bg-white focus:border-brand-400 focus:outline-none">
                <option value="">-- chọn field --</option>
                {numFields.map(f => <option key={f.k} value={f.k}>{f.l}</option>)}
              </select>
            </div>
          </>
        )}
        {ds && wg.type === 'table' && (
          <div>
            <label className="text-[10px] text-gray-400 font-medium block mb-1">Cột hiển thị</label>
            <div className="space-y-1">
              {allFields.map(f => {
                const sel = wg.cols?.includes(f.k) ?? false;
                return (
                  <div key={f.k} onClick={() => { const c = wg.cols ?? []; onUpdate({ cols: sel ? c.filter(k => k !== f.k) : [...c, f.k] }); }}
                    className={`flex items-center gap-2 rounded border px-2 py-1 cursor-pointer transition-colors ${sel ? 'border-brand-300 bg-brand-50' : 'border-gray-200 hover:border-brand-200'}`}>
                    <span className="font-mono text-[10px] flex-1 text-gray-600">{f.k}</span>
                    <span className={`text-[9px] px-1 rounded ${ftCls(f.t)}`}>{f.t}</span>
                  </div>
                );
              })}
            </div>
          </div>
        )}
        {ds && wg.type === 'pie' && (
          <>
            <div>
              <label className="text-[10px] text-gray-400 font-medium block mb-1">Danh mục</label>
              <select value={wg.catField ?? ''} onChange={e => onUpdate({ catField: e.target.value })}
                className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 bg-white focus:border-brand-400 focus:outline-none">
                <option value="">-- chọn field --</option>
                {allFields.map(f => <option key={f.k} value={f.k}>{f.l}</option>)}
              </select>
            </div>
            <div>
              <label className="text-[10px] text-gray-400 font-medium block mb-1">Giá trị</label>
              <select value={wg.valField ?? ''} onChange={e => onUpdate({ valField: e.target.value })}
                className="w-full text-xs rounded border border-gray-200 px-2 py-1.5 bg-white focus:border-brand-400 focus:outline-none">
                <option value="">-- chọn field --</option>
                {numFields.map(f => <option key={f.k} value={f.k}>{f.l}</option>)}
              </select>
            </div>
          </>
        )}

        {/* Span */}
        <div>
          <label className="text-[10px] text-gray-400 font-medium block mb-1">Độ rộng (/ 12 cột)</label>
          <div className="flex gap-1 flex-wrap">
            {[2,3,4,6,8,12].map(n => (
              <button key={n} onClick={() => onUpdate({ span: n })}
                className={`px-2 py-1 rounded text-xs border transition-colors ${wg.span === n ? 'bg-brand-600 text-white border-brand-600' : 'border-gray-200 text-gray-500 hover:border-brand-300'}`}>
                {n}
              </button>
            ))}
          </div>
        </div>

        {/* Color */}
        <div>
          <label className="text-[10px] text-gray-400 font-medium block mb-1">Màu sắc</label>
          <div className="flex gap-1.5 flex-wrap">
            {COLORS.map(c => (
              <button key={c} onClick={() => onUpdate({ color: c })}
                className={`w-5 h-5 rounded-full border-2 transition-all ${wg.color === c ? 'border-gray-700 scale-110' : 'border-transparent'}`}
                style={{ background: c }}/>
            ))}
          </div>
        </div>
      </div>
      <div className="p-3 border-t border-gray-100">
        <button onClick={onDelete}
          className="w-full rounded-lg bg-red-50 py-1.5 text-xs text-red-600 hover:bg-red-100 transition-colors font-medium">
          🗑 Xóa widget
        </button>
      </div>
    </div>
  );
}

// ── ScreenDesigner ─────────────────────────────────────────────────────────────

interface DesignerState {
  menuId: string; screenId: string | null;
  screenName: string; screenIcon: string;
  widgets: DesignerWidget[]; selWgId: string | null; palDs: string;
}

function ScreenDesigner({
  state, onClose, onSave, saving,
}: {
  state: DesignerState;
  onClose: () => void;
  onSave: (s: DesignerState) => void;
  saving: boolean;
}) {
  const [widgets, setWidgets] = useState<DesignerWidget[]>(state.widgets);
  const [selWgId, setSelWgId] = useState<string | null>(state.selWgId);
  const [screenName, setScreenName] = useState(state.screenName);
  const [palDs, setPalDs] = useState(state.palDs);
  const [dropInd, setDropInd] = useState<{ id: string; side: 'before' | 'after' } | null>(null);

  const dragRef = useRef<{ fromPal: boolean; palType: string; fromCv: boolean; cvWgId: string }>({ fromPal:false, palType:'', fromCv:false, cvWgId:'' });
  const rszRef = useRef<{ wgId:string; startX:number; startSpan:number; colW:number } | null>(null);
  const gridRef = useRef<HTMLDivElement>(null);

  // ─ Resize ──
  const startRsz = useCallback((e: React.MouseEvent, wgId: string) => {
    e.preventDefault(); e.stopPropagation();
    const wg = widgets.find(w => w.id === wgId);
    if (!wg || !gridRef.current) return;
    rszRef.current = { wgId, startX: e.clientX, startSpan: wg.span, colW: gridRef.current.offsetWidth / 12 };
    setSelWgId(wgId);
    document.body.style.cursor = 'se-resize';
    const onMove = (ev: MouseEvent) => {
      if (!rszRef.current) return;
      const { wgId: id, startX, startSpan, colW } = rszRef.current;
      const ns = Math.max(2, Math.min(12, Math.round(startSpan + (ev.clientX - startX) / colW)));
      setWidgets(ws => ws.map(w => w.id === id ? { ...w, span: ns } : w));
    };
    const onUp = () => {
      document.body.style.cursor = '';
      rszRef.current = null;
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }, [widgets]);

  // ─ DnD palette ──
  const palDragStart = (e: React.DragEvent, type: string) => {
    dragRef.current = { fromPal:true, palType:type, fromCv:false, cvWgId:'' };
    e.dataTransfer.effectAllowed = 'copy';
    e.dataTransfer.setData('text/plain', type);
  };

  // ─ DnD canvas widget ──
  const cvWgDragStart = (e: React.DragEvent, wgId: string) => {
    dragRef.current = { fromPal:false, palType:'', fromCv:true, cvWgId:wgId };
    e.dataTransfer.effectAllowed = 'move';
    e.stopPropagation();
  };
  const cvWgDragOver = (e: React.DragEvent, targetId: string) => {
    e.preventDefault(); e.stopPropagation();
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
    const side = e.clientX < rect.left + rect.width / 2 ? 'before' : 'after';
    setDropInd({ id: targetId, side });
  };
  const cvWgDragLeave = () => setDropInd(null);
  const cvWgDrop = (e: React.DragEvent, targetId: string) => {
    e.preventDefault(); e.stopPropagation();
    const { fromPal, palType, fromCv, cvWgId } = dragRef.current;
    const insertBefore = dropInd?.side !== 'after';
    setDropInd(null);
    if (fromPal && palType) {
      addWg(palType, targetId, insertBefore);
    } else if (fromCv && cvWgId && cvWgId !== targetId) {
      setWidgets(ws => {
        const a = [...ws];
        const si = a.findIndex(w => w.id === cvWgId);
        if (si === -1) return ws;
        const [wg] = a.splice(si, 1);
        const ti = a.findIndex(w => w.id === targetId);
        if (ti === -1) { a.push(wg); return a; }
        a.splice(insertBefore ? ti : ti + 1, 0, wg);
        return a;
      });
    }
    dragRef.current = { fromPal:false, palType:'', fromCv:false, cvWgId:'' };
  };

  // ─ Canvas bg drop ──
  const cvBgDrop = (e: React.DragEvent) => {
    e.preventDefault();
    if ((e.target as HTMLElement).closest('[data-wgid]')) return;
    const { fromPal, palType, fromCv, cvWgId } = dragRef.current;
    setDropInd(null);
    if (fromPal && palType) addWg(palType, null, true);
    else if (fromCv && cvWgId) {
      setWidgets(ws => {
        const a = [...ws]; const i = a.findIndex(w => w.id === cvWgId);
        if (i === -1) return ws;
        const [wg] = a.splice(i, 1); a.push(wg); return a;
      });
    }
    dragRef.current = { fromPal:false, palType:'', fromCv:false, cvWgId:'' };
  };

  const addWg = (type: string, beforeId: string | null, insertBefore: boolean) => {
    const wg: DesignerWidget = {
      id: uid(), type, title: defaultTitle(type), span: defaultSpan(type),
      color: COLORS[widgets.length % COLORS.length], ds: palDs,
    };
    setWidgets(ws => {
      if (!beforeId) return [...ws, wg];
      const a = [...ws];
      const ti = a.findIndex(w => w.id === beforeId);
      if (ti === -1) return [...a, wg];
      a.splice(insertBefore ? ti : ti + 1, 0, wg);
      return a;
    });
    setSelWgId(wg.id);
  };

  const selWg = widgets.find(w => w.id === selWgId) ?? null;

  return (
    <div className="flex h-full flex-col">
      {/* Designer toolbar */}
      <div className="flex items-center gap-3 border-b border-gray-200 bg-white px-4 py-2.5 flex-shrink-0">
        <button onClick={onClose} className="flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs text-gray-500 hover:bg-gray-100 transition-colors">
          ← Quay lại
        </button>
        <div className="h-5 w-px bg-gray-200"/>
        <span className="text-sm font-semibold text-gray-700">Thiết kế màn hình</span>
        <div className="flex-1"/>
        <input value={screenName} onChange={e => setScreenName(e.target.value)}
          className="w-52 rounded-lg border border-gray-200 px-3 py-1.5 text-xs focus:border-brand-400 focus:outline-none"
          placeholder="Tên màn hình..."/>
        <button onClick={() => onSave({ ...state, widgets, screenName, selWgId, palDs })}
          disabled={saving}
          className="flex items-center gap-1.5 rounded-lg bg-brand-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-brand-700 disabled:opacity-60 transition-colors">
          {saving ? '💾 Đang lưu…' : '💾 Lưu'}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Palette */}
        <div className="w-44 flex-shrink-0 border-r border-gray-200 bg-gray-50 flex flex-col overflow-y-auto">
          <div className="p-3">
            <p className="mb-2 text-[10px] font-bold uppercase tracking-wider text-gray-400">Loại Widget</p>
            {[['kpi','🔢','KPI Card'],['line','📈','Line Chart'],['bar','📊','Bar Chart'],['pie','🥧','Pie Chart'],['table','📋','Bảng dữ liệu'],['text','✏️','Văn bản']].map(([t,ic,nm]) => (
              <div key={t} draggable onDragStart={e => palDragStart(e, t)}
                className="mb-1 flex items-center gap-2 rounded-lg border border-gray-200 bg-white px-2.5 py-2 cursor-grab text-xs hover:border-brand-400 hover:bg-brand-50 transition-colors">
                <span>{ic}</span><span className="text-gray-600 font-medium">{nm}</span>
              </div>
            ))}
          </div>
          <div className="border-t border-gray-200 p-3">
            <p className="mb-2 text-[10px] font-bold uppercase tracking-wider text-gray-400">Nguồn dữ liệu</p>
            <select value={palDs} onChange={e => setPalDs(e.target.value)}
              className="w-full rounded border border-gray-200 bg-white px-2 py-1.5 text-[10px] focus:border-brand-400 focus:outline-none">
              <option value="">-- Chọn provider --</option>
              <optgroup label="SQL">
                {['sql.sales_monthly','sql.top_products','sql.kpi_summary','sql.orders_recent','sql.regional_stats'].map(k => <option key={k} value={k}>{DS[k].label}</option>)}
              </optgroup>
              <optgroup label="Excel"><option value="excel.sales_dashboard">{DS['excel.sales_dashboard'].label}</option></optgroup>
              <optgroup label="gRPC / ML">
                {['ml.fraud.score','ml.fraud.batchScore'].map(k => <option key={k} value={k}>{DS[k].label}</option>)}
              </optgroup>
            </select>
            {palDs && DS[palDs] && (
              <div className="mt-2">
                <span className={`text-[9px] rounded px-1.5 py-0.5 font-semibold ${dsCls(DS[palDs].type)}`}>{DS[palDs].type} · {DS[palDs].provider}</span>
                <div className="mt-1.5 flex flex-wrap gap-1">
                  {DS[palDs].fields.map(f => <span key={f.k} className="text-[9px] font-mono bg-gray-100 rounded px-1 text-gray-500">{f.k}</span>)}
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Canvas */}
        <div className="flex-1 overflow-auto bg-gray-100 p-4"
          onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; }}
          onDrop={cvBgDrop}>
          {widgets.length === 0 ? (
            <div className="flex h-full flex-col items-center justify-center gap-3 text-gray-400 pointer-events-none">
              <div className="text-4xl">🎨</div>
              <p className="text-sm">Canvas trống — kéo widget từ bảng trái vào đây</p>
            </div>
          ) : (
            <div ref={gridRef}
              className="grid gap-3"
              style={{ gridTemplateColumns: 'repeat(12, 1fr)', alignContent: 'start', minHeight: 400 }}>
              {widgets.map(wg => (
                <div key={wg.id}
                  data-wgid={wg.id}
                  draggable
                  onDragStart={e => cvWgDragStart(e, wg.id)}
                  onDragOver={e => cvWgDragOver(e, wg.id)}
                  onDragLeave={cvWgDragLeave}
                  onDrop={e => cvWgDrop(e, wg.id)}
                  onClick={() => setSelWgId(wg.id)}
                  className={`rounded-xl border bg-white p-3 cursor-pointer transition-all relative overflow-hidden
                    ${selWgId === wg.id ? 'border-brand-400 shadow-md ring-2 ring-brand-200' : 'border-gray-200 hover:border-gray-300'}
                    ${dropInd?.id === wg.id && dropInd.side === 'before' ? 'ring-2 ring-brand-400 ring-offset-0 [box-shadow:-4px_0_0_0_#4f46e5]' : ''}
                    ${dropInd?.id === wg.id && dropInd.side === 'after' ? 'ring-2 ring-brand-400 ring-offset-0 [box-shadow:4px_0_0_0_#4f46e5]' : ''}`}
                  style={{ gridColumn: `span ${wg.span}`, minHeight: 100 }}>
                  {/* Drag handle */}
                  <span className="absolute top-2 left-2 text-gray-300 cursor-grab text-sm">⠿</span>
                  {/* Delete button */}
                  <button onClick={e => { e.stopPropagation(); setWidgets(ws => ws.filter(w => w.id !== wg.id)); if (selWgId === wg.id) setSelWgId(null); }}
                    className="absolute top-1.5 right-1.5 rounded px-1.5 py-0.5 text-[10px] text-gray-300 hover:bg-red-50 hover:text-red-500 opacity-0 group-hover:opacity-100 transition-all [.hover_&]:opacity-100"
                    style={{ opacity: selWgId === wg.id ? 1 : undefined }}>
                    ✕
                  </button>
                  {/* Title */}
                  <p className="text-[10px] font-semibold text-gray-500 truncate pl-4 pr-4 mb-1.5">{wg.title}</p>
                  {/* Preview */}
                  <WidgetPreview wg={wg}/>
                  {/* DS badge */}
                  {wg.ds && DS[wg.ds] && (
                    <div className="mt-1.5 flex items-center gap-1">
                      <span className="inline-block w-1.5 h-1.5 rounded-full" style={{ background: wg.color }}/>
                      <span className="text-[9px] text-gray-400 truncate">{DS[wg.ds].label}</span>
                    </div>
                  )}
                  {/* Resize handle */}
                  <div className="absolute bottom-1.5 right-1.5 w-4 h-4 cursor-se-resize flex items-end justify-end text-gray-300 hover:text-brand-400"
                    onMouseDown={e => startRsz(e, wg.id)}>
                    <span className="text-xs leading-none">◢</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Config panel */}
        <div className="w-52 flex-shrink-0 border-l border-gray-200 bg-white overflow-hidden">
          {selWg ? (
            <ConfigPanel
              wg={selWg}
              onUpdate={patch => setWidgets(ws => ws.map(w => w.id === selWg.id ? { ...w, ...patch } : w))}
              onDelete={() => { setWidgets(ws => ws.filter(w => w.id !== selWg.id)); setSelWgId(null); }}
            />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-2 p-4 text-center">
              <span className="text-2xl">👆</span>
              <p className="text-xs text-gray-400">Chọn một widget để cấu hình</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── TreeNode ───────────────────────────────────────────────────────────────────

function TreeNode({
  node, allMenus, selectedId, expanded, onSelect, onToggleExpand,
}: {
  node: AdminMenuNode; allMenus: AdminMenuNode[]; selectedId: string | null;
  expanded: Set<string>;
  onSelect: (id: string) => void;
  onToggleExpand: (id: string) => void;
}) {
  const children = allMenus.filter(m => m.parentId === node.id).sort((a,b) => a.sortOrder - b.sortOrder);
  const isOpen = expanded.has(node.id);

  return (
    <div>
      <button onClick={() => onSelect(node.id)}
        className={`flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors ${
          selectedId === node.id ? 'bg-brand-600 text-white' : 'text-gray-700 hover:bg-gray-100'
        }`}>
        {children.length > 0 ? (
          <span className="shrink-0 text-gray-400 text-xs w-3"
            onClick={e => { e.stopPropagation(); onToggleExpand(node.id); }}>
            {isOpen ? '▾' : '▸'}
          </span>
        ) : <span className="w-3 shrink-0"/>}
        <span className="text-base leading-none">{node.icon}</span>
        <span className="flex-1 truncate font-medium">{node.name}</span>
        {node.screenCount > 0 && (
          <span className={`shrink-0 rounded-full px-1.5 py-0.5 text-[10px] ${selectedId === node.id ? 'bg-brand-500 text-white' : 'bg-gray-100 text-gray-500'}`}>
            {node.screenCount}
          </span>
        )}
        {!node.isVisible && (
          <span className="shrink-0 rounded bg-gray-200 px-1 py-0.5 text-[10px] text-gray-500">Ẩn</span>
        )}
      </button>
      {children.length > 0 && isOpen && (
        <div className="ml-5 mt-0.5 border-l border-gray-200 pl-2 space-y-0.5">
          {children.map(c => (
            <TreeNode key={c.id} node={c} allMenus={allMenus} selectedId={selectedId}
              expanded={expanded} onSelect={onSelect} onToggleExpand={onToggleExpand}/>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

export function MenuManager() {
  // ─ API data ──
  const [menus, setMenus]   = useState<AdminMenuNode[]>([]);
  const [screens, setScreens] = useState<AdminScreen[]>([]);
  const [perms, setPerms]   = useState<AdminPermission[]>([]);

  // ─ UI state ──
  const [selId, setSelId]   = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [tab, setTab]       = useState<'screens' | 'perms' | 'info'>('screens');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toastMsg, setToastMsg] = useState('');
  const [toastVis, setToastVis] = useState(false);

  // ─ Info edit buffer ──
  const [infoEdit, setInfoEdit] = useState<Partial<AdminMenuNode> | null>(null);

  // ─ Designer ──
  const [designer, setDesigner] = useState<DesignerState | null>(null);

  // ─ New menu modal ──
  const [showModal, setShowModal] = useState(false);
  const [newMenu, setNewMenu] = useState({ name:'', slug:'', icon:'📊', desc:'', parentId:'', visible:true });

  // ─ Perm add ──
  const [showAddPerm, setShowAddPerm] = useState(false);
  const [permKind, setPermKind] = useState<'role'|'user'>('role');
  const [permVal, setPermVal] = useState('');

  const selMenu = menus.find(m => m.id === selId) ?? null;

  // ─ Toast helper ──
  const toast = useCallback((msg: string) => {
    setToastMsg(msg); setToastVis(true);
    setTimeout(() => setToastVis(false), 2200);
  }, []);

  // ─ Load menus ──
  const loadMenus = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiGet<AdminMenuNode[]>('/api/v1/admin/menus');
      setMenus(data);
      if (!selId && data.length > 0) setSelId(data[0].id);
    } catch (e) {
      toast('Lỗi tải danh sách menu: ' + (e instanceof Error ? e.message : ''));
    } finally {
      setLoading(false);
    }
  }, [selId, toast]);

  // ─ Load screens ──
  const loadScreens = useCallback(async (menuId: string) => {
    try {
      const data = await apiGet<AdminScreen[]>(`/api/v1/admin/menus/${menuId}/screens`);
      setScreens(data);
    } catch { setScreens([]); }
  }, []);

  // ─ Load perms ──
  const loadPerms = useCallback(async (menuId: string) => {
    try {
      const data = await apiGet<AdminPermission[]>(`/api/v1/admin/menus/${menuId}/permissions`);
      setPerms(data);
    } catch { setPerms([]); }
  }, []);

  // ─ Init ──
  useEffect(() => { void loadMenus(); }, []);  // eslint-disable-line react-hooks/exhaustive-deps

  // ─ Reload screens+perms when selection changes ──
  useEffect(() => {
    if (selId) { void loadScreens(selId); void loadPerms(selId); }
    setInfoEdit(null);
  }, [selId, loadScreens, loadPerms]);

  // ─ Select menu ──
  const selectMenu = (id: string) => { setSelId(id); setTab('screens'); setDesigner(null); };
  const toggleExpand = (id: string) =>
    setExpanded(s => { const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n; });

  // ─ Create menu ──
  const handleCreateMenu = async () => {
    if (!newMenu.name.trim()) return;
    setSaving(true);
    try {
      const res = await apiPost<object, { id: string }>('/api/v1/admin/menus', {
        name: newMenu.name.trim(), slug: newMenu.slug || slugify(newMenu.name),
        icon: newMenu.icon, description: newMenu.desc || null,
        parentId: newMenu.parentId || null, sortOrder: 0, isVisible: newMenu.visible,
      });
      await loadMenus();
      setSelId(res.id);
      setShowModal(false);
      setNewMenu({ name:'', slug:'', icon:'📊', desc:'', parentId:'', visible:true });
      toast('✓ Đã tạo menu "' + newMenu.name + '"');
    } catch (e) {
      toast('Lỗi tạo menu: ' + (e instanceof Error ? e.message : ''));
    } finally { setSaving(false); }
  };

  // ─ Save menu info ──
  const handleSaveInfo = async () => {
    if (!selId || !infoEdit) return;
    setSaving(true);
    try {
      await apiPut(`/api/v1/admin/menus/${selId}`, infoEdit);
      await loadMenus();
      setInfoEdit(null);
      toast('✓ Đã lưu thông tin menu');
    } catch (e) {
      toast('Lỗi lưu: ' + (e instanceof Error ? e.message : ''));
    } finally { setSaving(false); }
  };

  // ─ Delete menu ──
  const handleDeleteMenu = async () => {
    if (!selId || !selMenu) return;
    if (!window.confirm(`Xóa menu "${selMenu.name}"? Tất cả màn hình và widget trong menu này sẽ bị xóa.`)) return;
    setSaving(true);
    try {
      await apiDelete(`/api/v1/admin/menus/${selId}`);
      setSelId(null); setDesigner(null);
      await loadMenus();
      toast('Đã xóa menu');
    } catch (e) {
      toast('Lỗi xóa: ' + (e instanceof Error ? e.message : ''));
    } finally { setSaving(false); }
  };

  // ─ Create screen ──
  const handleCreateScreen = async () => {
    if (!selId) return;
    const name = window.prompt('Tên màn hình mới:');
    if (!name?.trim()) return;
    try {
      await apiPost(`/api/v1/admin/menus/${selId}/screens`, { name: name.trim(), icon:'📊', status:'draft', sortOrder: screens.length });
      await loadScreens(selId);
      await loadMenus();
      toast('✓ Đã tạo màn hình "' + name + '"');
    } catch (e) { toast('Lỗi: ' + (e instanceof Error ? e.message : '')); }
  };

  // ─ Delete screen ──
  const handleDeleteScreen = async (screenId: string, screenName: string) => {
    if (!selId) return;
    if (!window.confirm(`Xóa màn hình "${screenName}"?`)) return;
    try {
      await apiDelete(`/api/v1/admin/menus/${selId}/screens/${screenId}`);
      await loadScreens(selId); await loadMenus();
      toast('Đã xóa màn hình');
    } catch (e) { toast('Lỗi: ' + (e instanceof Error ? e.message : '')); }
  };

  // ─ Open designer ──
  const openDesigner = async (sc: AdminScreen) => {
    if (!selId) return;
    try {
      const wgData = await apiGet<WidgetDef[]>(`/api/v1/admin/menus/${selId}/screens/${sc.id}/widgets`);
      const widgets: DesignerWidget[] = wgData.map(w => {
        const cfg = (() => { try { return JSON.parse(w.config) as Record<string,unknown>; } catch { return {}; } })();
        return { id: w.id, type: w.widgetType, title: w.title, span: w.colSpan,
          color: w.color, ds: w.dataSource ?? '',
          xField: cfg.xField as string, yField: cfg.yField as string,
          valField: cfg.valField as string, trendField: cfg.trendField as string,
          catField: cfg.catField as string, cols: cfg.cols as string[],
        };
      });
      setDesigner({ menuId: selId, screenId: sc.id, screenName: sc.name, screenIcon: sc.icon, widgets, selWgId: null, palDs: '' });
    } catch (e) { toast('Lỗi mở designer: ' + (e instanceof Error ? e.message : '')); }
  };

  // ─ Save designer ──
  const handleSaveDesigner = async (s: DesignerState) => {
    setSaving(true);
    try {
      const body = {
        name: s.screenName, icon: s.screenIcon || '📊', status: 'published',
        widgets: s.widgets.map((w, i) => ({
          widgetType: w.type, title: w.title, colSpan: w.span, sortOrder: i,
          color: w.color, dataSource: w.ds || null,
          config: JSON.stringify({
            xField: w.xField, yField: w.yField, valField: w.valField,
            trendField: w.trendField, catField: w.catField, cols: w.cols,
          }),
        })),
      };
      const url = s.screenId
        ? `/api/v1/admin/menus/${s.menuId}/screens/${s.screenId}/save`
        : `/api/v1/admin/menus/${s.menuId}/screens`;
      if (s.screenId) await apiPut(url, body);
      else await apiPost(url, body);
      await loadScreens(s.menuId); await loadMenus();
      setDesigner(null);
      toast('✓ Đã lưu màn hình "' + s.screenName + '"');
    } catch (e) {
      toast('Lỗi lưu: ' + (e instanceof Error ? e.message : ''));
    } finally { setSaving(false); }
  };

  // ─ Permissions ──
  const handleAddPerm = async () => {
    if (!selId || !permVal) return;
    try {
      await apiPost(`/api/v1/admin/menus/${selId}/permissions`, {
        principalType: permKind, principalValue: permVal, canView: true, canExport: false,
      });
      await loadPerms(selId);
      setPermVal(''); setShowAddPerm(false);
      toast('✓ Đã thêm quyền');
    } catch (e) { toast('Lỗi: ' + (e instanceof Error ? e.message : '')); }
  };
  const handleTogglePerm = async (p: AdminPermission, field: 'canView' | 'canExport') => {
    if (!selId) return;
    try {
      await apiPut(`/api/v1/admin/menus/${selId}/permissions/${p.id}`, { [field]: !p[field] });
      setPerms(ps => ps.map(x => x.id === p.id ? { ...x, [field]: !x[field] } : x));
    } catch (e) { toast('Lỗi: ' + (e instanceof Error ? e.message : '')); }
  };
  const handleDeletePerm = async (p: AdminPermission) => {
    if (!selId) return;
    try {
      await apiDelete(`/api/v1/admin/menus/${selId}/permissions/${p.id}`);
      setPerms(ps => ps.filter(x => x.id !== p.id));
      toast('Đã xóa quyền');
    } catch (e) { toast('Lỗi: ' + (e instanceof Error ? e.message : '')); }
  };

  const infoVal = infoEdit ? { ...selMenu, ...infoEdit } as AdminMenuNode : selMenu;

  // ── DESIGNER VIEW ──
  if (designer) {
    return (
      <div className="flex h-full flex-col -m-6">
        <ScreenDesigner state={designer} onClose={() => setDesigner(null)} onSave={handleSaveDesigner} saving={saving}/>
      </div>
    );
  }

  const tabCls = (t: typeof tab) =>
    `px-4 py-2.5 text-sm font-medium border-b-2 transition-colors ${
      tab === t ? 'border-brand-600 text-brand-700' : 'border-transparent text-gray-500 hover:text-gray-700'
    }`;

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="mb-5 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Quản lý Menu Báo cáo</h1>
          <p className="mt-0.5 text-sm text-gray-500">Tạo, thiết kế màn hình và phân quyền truy cập</p>
        </div>
        <button onClick={() => setShowModal(true)}
          className="flex items-center gap-2 rounded-xl bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-brand-700 transition-colors">
          + Tạo menu mới
        </button>
      </div>

      <div className="flex flex-1 gap-5 overflow-hidden min-h-0">

        {/* Tree sidebar */}
        <aside className="flex w-60 shrink-0 flex-col rounded-2xl border border-gray-200 bg-white shadow-sm overflow-hidden">
          <div className="border-b border-gray-100 px-4 py-3">
            <p className="text-xs font-semibold uppercase tracking-widest text-gray-400">Cây menu</p>
          </div>
          <div className="flex-1 overflow-y-auto p-3 space-y-0.5">
            {loading ? (
              <div className="flex items-center justify-center py-10 text-gray-400 text-sm">Đang tải…</div>
            ) : menus.length === 0 ? (
              <div className="py-10 text-center text-xs text-gray-400">Chưa có menu nào.<br/>Nhấn <strong>+ Tạo menu mới</strong>.</div>
            ) : (
              menus.filter(m => !m.parentId).sort((a,b) => a.sortOrder - b.sortOrder).map(m => (
                <TreeNode key={m.id} node={m} allMenus={menus} selectedId={selId}
                  expanded={expanded} onSelect={selectMenu} onToggleExpand={toggleExpand}/>
              ))
            )}
          </div>
          <div className="border-t border-gray-100 p-3">
            <button onClick={() => setShowModal(true)}
              className="flex w-full items-center justify-center gap-1 rounded-lg border border-dashed border-gray-300 py-2 text-xs text-gray-500 hover:border-brand-400 hover:text-brand-600 transition-colors">
              + Thêm menu
            </button>
          </div>
        </aside>

        {/* Editor */}
        {selMenu && infoVal ? (
          <div className="flex flex-1 flex-col rounded-2xl border border-gray-200 bg-white shadow-sm overflow-hidden min-w-0">
            {/* Editor header */}
            <div className="flex items-center gap-3 border-b border-gray-100 px-6 py-4">
              <span className="text-2xl">{selMenu.icon}</span>
              <div className="flex-1 min-w-0">
                <h2 className="text-base font-bold text-gray-900 truncate">{selMenu.name}</h2>
                <p className="text-xs text-gray-400">/reports/{selMenu.slug}</p>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <span className="rounded-full bg-blue-50 px-2.5 py-1 text-xs text-blue-600">
                  {selMenu.screenCount} màn hình
                </span>
                <span className="rounded-full bg-purple-50 px-2.5 py-1 text-xs text-purple-600">
                  {perms.length} quyền
                </span>
                <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${selMenu.isVisible ? 'bg-green-50 text-green-600' : 'bg-gray-100 text-gray-500'}`}>
                  {selMenu.isVisible ? '● Hiển thị' : '○ Ẩn'}
                </span>
              </div>
            </div>

            {/* Tabs */}
            <div className="flex border-b border-gray-100 px-6">
              <button className={tabCls('screens')} onClick={() => setTab('screens')}>
                📋 Màn hình {screens.length > 0 && <span className="ml-1.5 rounded-full bg-gray-100 px-1.5 py-0.5 text-xs text-gray-600">{screens.length}</span>}
              </button>
              <button className={tabCls('perms')} onClick={() => setTab('perms')}>
                🔐 Phân quyền {perms.length > 0 && <span className="ml-1.5 rounded-full bg-gray-100 px-1.5 py-0.5 text-xs text-gray-600">{perms.length}</span>}
              </button>
              <button className={tabCls('info')} onClick={() => setTab('info')}>⚙️ Thông tin</button>
            </div>

            {/* Tab content */}
            <div className="flex-1 overflow-y-auto p-6" key={`${selId}-${tab}`}>

              {/* ── Tab Màn hình ── */}
              {tab === 'screens' && (
                <div className="space-y-3">
                  <div className="rounded-xl bg-blue-50 border border-blue-100 p-3 text-xs text-blue-700">
                    📊 Mỗi màn hình là một trang báo cáo gồm các widget (KPI, biểu đồ, bảng). Nhấn <strong>✏ Thiết kế</strong> để mở Screen Designer.
                  </div>
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-gray-700">Màn hình trong "{selMenu.name}"</p>
                    <button onClick={handleCreateScreen}
                      className="rounded-lg bg-brand-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-brand-700 transition-colors">
                      + Tạo màn hình mới
                    </button>
                  </div>
                  {screens.length === 0 ? (
                    <div className="rounded-xl border-2 border-dashed border-gray-200 py-12 text-center text-sm text-gray-400">
                      <p className="text-3xl mb-2">📋</p>
                      <p>Chưa có màn hình — nhấn <strong className="text-gray-600">+ Tạo màn hình mới</strong></p>
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      {screens.map(sc => (
                        <div key={sc.id}
                          className="flex items-center gap-3 rounded-xl border border-gray-200 bg-white px-4 py-3 hover:border-brand-200 hover:bg-brand-50/30 transition-all cursor-pointer"
                          onClick={() => openDesigner(sc)}>
                          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-xl">{sc.icon}</div>
                          <div className="flex-1 min-w-0">
                            <p className="text-sm font-semibold text-gray-800 truncate">{sc.name}</p>
                            <div className="flex items-center gap-2 mt-0.5">
                              <span className={`text-[10px] rounded px-1.5 py-0.5 font-medium ${sc.status === 'published' ? 'bg-green-100 text-green-700' : 'bg-yellow-100 text-yellow-700'}`}>
                                {sc.status === 'published' ? 'Đã xuất bản' : 'Bản nháp'}
                              </span>
                              <span className="text-[10px] text-gray-400">{sc.widgetCount} widget</span>
                            </div>
                          </div>
                          <div className="flex gap-2 shrink-0" onClick={e => e.stopPropagation()}>
                            <button onClick={() => openDesigner(sc)}
                              className="rounded-lg border border-gray-200 px-2.5 py-1.5 text-xs text-gray-500 hover:border-brand-300 hover:text-brand-600 transition-colors">
                              ✏ Thiết kế
                            </button>
                            <button onClick={() => handleDeleteScreen(sc.id, sc.name)}
                              className="rounded-lg border border-gray-200 px-2 py-1.5 text-xs text-gray-300 hover:border-red-200 hover:text-red-500 transition-colors">
                              🗑
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {/* ── Tab Phân quyền ── */}
              {tab === 'perms' && (
                <div className="space-y-4">
                  <div className="rounded-xl bg-amber-50 border border-amber-100 p-3 text-xs text-amber-700">
                    ⚠️ Người dùng chỉ thấy menu nếu được cấp quyền <strong>Xem</strong>. Quyền <strong>Xuất</strong> cho phép tải dữ liệu ra file.
                  </div>
                  {perms.length > 0 && (
                    <div className="overflow-hidden rounded-xl border border-gray-200">
                      <table className="w-full text-sm">
                        <thead className="bg-gray-50">
                          <tr>
                            <th className="px-4 py-2.5 text-left text-xs font-semibold text-gray-500">Đối tượng</th>
                            <th className="px-4 py-2.5 text-center text-xs font-semibold text-gray-500 w-20">Xem</th>
                            <th className="px-4 py-2.5 text-center text-xs font-semibold text-gray-500 w-20">Xuất</th>
                            <th className="w-10 px-2"/>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                          {perms.map(p => (
                            <tr key={p.id} className="hover:bg-gray-50 transition-colors">
                              <td className="px-4 py-3">
                                <div className="flex items-center gap-2">
                                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${p.principalType === 'role' ? 'bg-purple-100 text-purple-700' : 'bg-blue-100 text-blue-700'}`}>
                                    {p.principalType === 'role' ? '🔐 Role' : '👤 User'}
                                  </span>
                                  <span className="font-medium text-gray-800">{p.principalValue}</span>
                                </div>
                              </td>
                              <td className="px-4 py-3 text-center">
                                <button onClick={() => handleTogglePerm(p, 'canView')}
                                  className={`rounded-full w-6 h-6 text-xs font-bold transition-colors ${p.canView ? 'bg-green-500 text-white' : 'bg-gray-200 text-gray-400'}`}>
                                  {p.canView ? '✓' : '✗'}
                                </button>
                              </td>
                              <td className="px-4 py-3 text-center">
                                <button onClick={() => handleTogglePerm(p, 'canExport')}
                                  className={`rounded-full w-6 h-6 text-xs font-bold transition-colors ${p.canExport ? 'bg-green-500 text-white' : 'bg-gray-200 text-gray-400'}`}>
                                  {p.canExport ? '✓' : '✗'}
                                </button>
                              </td>
                              <td className="px-2 py-3">
                                <button onClick={() => handleDeletePerm(p)}
                                  className="rounded p-1 text-gray-300 hover:bg-red-50 hover:text-red-500 transition-colors">✕</button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                  {perms.length === 0 && (
                    <div className="rounded-xl border-2 border-dashed border-gray-200 py-8 text-center text-sm text-gray-400">
                      Chưa có quyền truy cập — menu này bị ẩn với tất cả người dùng.
                    </div>
                  )}
                  {showAddPerm ? (
                    <div className="rounded-xl border border-brand-200 bg-brand-50 p-4 space-y-3">
                      <p className="text-xs font-semibold text-brand-700">Thêm quyền truy cập</p>
                      <div className="flex gap-2">
                        {(['role','user'] as const).map(k => (
                          <button key={k} onClick={() => setPermKind(k)}
                            className={`rounded-lg px-3 py-1.5 text-xs font-medium transition-colors ${permKind === k ? 'bg-brand-600 text-white' : 'bg-white text-gray-600 border border-gray-200'}`}>
                            {k === 'role' ? '🔐 Theo Role' : '👤 Theo User'}
                          </button>
                        ))}
                      </div>
                      {permKind === 'role' ? (
                        <div className="flex flex-wrap gap-2">
                          {ROLE_OPTIONS.filter(r => !perms.some(p => p.principalType === 'role' && p.principalValue === r)).map(r => (
                            <button key={r} onClick={() => setPermVal(r)}
                              className={`rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors ${permVal === r ? 'border-brand-400 bg-brand-100 text-brand-700' : 'border-gray-200 bg-white text-gray-600 hover:border-brand-300'}`}>
                              {r}
                            </button>
                          ))}
                        </div>
                      ) : (
                        <input value={permVal} onChange={e => setPermVal(e.target.value)}
                          placeholder="Nhập username hoặc email..."
                          className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"/>
                      )}
                      <div className="flex gap-2">
                        <button onClick={handleAddPerm} disabled={!permVal}
                          className="rounded-lg bg-brand-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-40">Thêm</button>
                        <button onClick={() => { setShowAddPerm(false); setPermVal(''); }}
                          className="rounded-lg px-4 py-1.5 text-xs text-gray-500 hover:bg-gray-100">Hủy</button>
                      </div>
                    </div>
                  ) : (
                    <button onClick={() => setShowAddPerm(true)}
                      className="flex w-full items-center justify-center gap-2 rounded-xl border-2 border-dashed border-gray-200 py-3 text-sm text-gray-500 hover:border-brand-300 hover:text-brand-600 transition-colors">
                      + Thêm quyền truy cập
                    </button>
                  )}
                </div>
              )}

              {/* ── Tab Thông tin ── */}
              {tab === 'info' && infoVal && (
                <div className="space-y-5 max-w-xl">
                  {/* Icon + Name */}
                  <div className="flex gap-3">
                    <div className="relative">
                      <button onClick={() => {}} title="Chọn icon"
                        className="flex h-10 w-10 items-center justify-center rounded-lg border border-gray-300 text-xl hover:border-brand-500 hover:bg-brand-50">
                        {infoVal.icon}
                      </button>
                      <div className="absolute left-0 top-12 z-10 hidden group-hover:grid grid-cols-4 gap-1 rounded-xl border border-gray-200 bg-white p-2 shadow-lg">
                        {ICONS.map(ic => (
                          <button key={ic} onClick={() => setInfoEdit(p => ({ ...p, icon: ic }))}
                            className={`rounded p-1.5 text-xl hover:bg-gray-100 ${infoVal.icon === ic ? 'bg-brand-100' : ''}`}>{ic}</button>
                        ))}
                      </div>
                    </div>
                    <div className="flex-1">
                      <label className="mb-1 block text-xs font-medium text-gray-600">Tên menu *</label>
                      <input value={infoVal.name}
                        onChange={e => setInfoEdit(p => ({ ...p, name: e.target.value, slug: slugify(e.target.value) }))}
                        className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"/>
                    </div>
                  </div>
                  {/* Icon picker (visible) */}
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-600">Icon</label>
                    <div className="flex flex-wrap gap-1">
                      {ICONS.map(ic => (
                        <button key={ic} onClick={() => setInfoEdit(p => ({ ...p, icon: ic }))}
                          className={`rounded-lg p-1.5 text-xl hover:bg-gray-100 transition-colors ${infoVal.icon === ic ? 'bg-brand-100 ring-1 ring-brand-400' : 'border border-gray-200'}`}>{ic}</button>
                      ))}
                    </div>
                  </div>
                  {/* Slug */}
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-600">Đường dẫn (slug)</label>
                    <div className="flex items-center rounded-lg border border-gray-300 focus-within:border-brand-500 focus-within:ring-1 focus-within:ring-brand-500">
                      <span className="border-r border-gray-200 bg-gray-50 px-3 py-2 text-xs text-gray-400 rounded-l-lg">/reports/</span>
                      <input value={infoVal.slug}
                        onChange={e => setInfoEdit(p => ({ ...p, slug: e.target.value }))}
                        className="flex-1 px-3 py-2 text-sm focus:outline-none rounded-r-lg"/>
                    </div>
                  </div>
                  {/* Description */}
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-600">Mô tả</label>
                    <textarea value={infoVal.description ?? ''}
                      onChange={e => setInfoEdit(p => ({ ...p, description: e.target.value }))}
                      rows={2}
                      className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm resize-none focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"/>
                  </div>
                  {/* Order + Visible */}
                  <div className="flex items-center gap-6">
                    <div>
                      <label className="mb-1 block text-xs font-medium text-gray-600">Thứ tự</label>
                      <input type="number" min={0} value={infoVal.sortOrder}
                        onChange={e => setInfoEdit(p => ({ ...p, sortOrder: Number(e.target.value) }))}
                        className="w-20 rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"/>
                    </div>
                    <div>
                      <label className="mb-1 block text-xs font-medium text-gray-600">Trạng thái</label>
                      <button onClick={() => setInfoEdit(p => ({ ...p, isVisible: !infoVal.isVisible }))}
                        className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition-colors ${infoVal.isVisible ? 'border-green-300 bg-green-50 text-green-700' : 'border-gray-300 bg-gray-50 text-gray-500'}`}>
                        <span className={`h-2 w-2 rounded-full ${infoVal.isVisible ? 'bg-green-500' : 'bg-gray-400'}`}/>
                        {infoVal.isVisible ? 'Hiển thị' : 'Ẩn'}
                      </button>
                    </div>
                  </div>
                </div>
              )}
            </div>

            {/* Footer */}
            <div className="flex items-center justify-between border-t border-gray-100 px-6 py-4">
              <button onClick={handleDeleteMenu}
                className="flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm text-red-500 hover:bg-red-50 transition-colors">
                🗑 Xóa menu
              </button>
              {tab === 'info' && infoEdit && (
                <div className="flex gap-2">
                  <button onClick={() => setInfoEdit(null)}
                    className="rounded-xl border border-gray-200 px-4 py-2 text-sm text-gray-500 hover:bg-gray-50">Hủy</button>
                  <button onClick={handleSaveInfo} disabled={saving}
                    className="rounded-xl bg-brand-600 px-5 py-2 text-sm font-semibold text-white shadow-sm hover:bg-brand-700 disabled:opacity-60 transition-colors">
                    {saving ? 'Đang lưu…' : 'Lưu thay đổi'}
                  </button>
                </div>
              )}
            </div>
          </div>
        ) : !loading && (
          <div className="flex flex-1 items-center justify-center rounded-2xl border-2 border-dashed border-gray-200 text-center">
            <div>
              <p className="text-4xl mb-3">📋</p>
              <p className="text-sm font-medium text-gray-500">Chọn một menu để chỉnh sửa</p>
              <p className="mt-1 text-xs text-gray-400">hoặc nhấn <strong>+ Tạo menu mới</strong></p>
            </div>
          </div>
        )}
      </div>

      {/* ── Modal: New menu ── */}
      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
          onClick={e => { if (e.target === e.currentTarget) setShowModal(false); }}>
          <div className="w-full max-w-md rounded-2xl border border-gray-200 bg-white shadow-2xl overflow-hidden">
            <div className="flex items-center gap-3 border-b border-gray-100 px-6 py-4">
              <h3 className="flex-1 text-base font-bold text-gray-900">Tạo menu mới</h3>
              <button onClick={() => setShowModal(false)} className="text-gray-400 hover:text-gray-600 text-xl">✕</button>
            </div>
            <div className="px-6 py-5 space-y-4">
              {/* Icon picker */}
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Icon</label>
                <div className="flex flex-wrap gap-1">
                  {ICONS.map(ic => (
                    <button key={ic} onClick={() => setNewMenu(p => ({ ...p, icon: ic }))}
                      className={`rounded-lg p-1.5 text-xl hover:bg-gray-100 ${newMenu.icon === ic ? 'bg-brand-100 ring-1 ring-brand-400' : ''}`}>{ic}</button>
                  ))}
                </div>
              </div>
              {/* Name */}
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Tên menu *</label>
                <input value={newMenu.name}
                  onChange={e => setNewMenu(p => ({ ...p, name: e.target.value, slug: slugify(e.target.value) }))}
                  placeholder="Ví dụ: Báo cáo Kinh doanh"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
                  autoFocus/>
              </div>
              {/* Slug */}
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Slug</label>
                <div className="flex items-center rounded-lg border border-gray-300 focus-within:border-brand-500 focus-within:ring-1 focus-within:ring-brand-500">
                  <span className="border-r border-gray-200 bg-gray-50 px-3 py-2 text-xs text-gray-400 rounded-l-lg">/reports/</span>
                  <input value={newMenu.slug}
                    onChange={e => setNewMenu(p => ({ ...p, slug: e.target.value }))}
                    className="flex-1 px-3 py-2 text-sm focus:outline-none rounded-r-lg"/>
                </div>
              </div>
              {/* Parent */}
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Menu cha</label>
                <select value={newMenu.parentId} onChange={e => setNewMenu(p => ({ ...p, parentId: e.target.value }))}
                  className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm focus:border-brand-500 focus:outline-none">
                  <option value="">-- Không có (menu gốc) --</option>
                  {menus.map(m => <option key={m.id} value={m.id}>{m.icon} {m.name}</option>)}
                </select>
              </div>
              {/* Description */}
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Mô tả</label>
                <textarea value={newMenu.desc}
                  onChange={e => setNewMenu(p => ({ ...p, desc: e.target.value }))}
                  rows={2}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm resize-none focus:border-brand-500 focus:outline-none"/>
              </div>
              {/* Visible */}
              <button onClick={() => setNewMenu(p => ({ ...p, visible: !p.visible }))}
                className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition-colors ${newMenu.visible ? 'border-green-300 bg-green-50 text-green-700' : 'border-gray-300 bg-gray-50 text-gray-500'}`}>
                <span className={`h-2 w-2 rounded-full ${newMenu.visible ? 'bg-green-500' : 'bg-gray-400'}`}/>
                {newMenu.visible ? 'Hiển thị công khai' : 'Ẩn (chỉ admin thấy)'}
              </button>
            </div>
            <div className="flex justify-end gap-3 border-t border-gray-100 px-6 py-4">
              <button onClick={() => setShowModal(false)}
                className="rounded-xl border border-gray-200 px-5 py-2 text-sm text-gray-500 hover:bg-gray-50">Hủy</button>
              <button onClick={handleCreateMenu} disabled={!newMenu.name.trim() || saving}
                className="rounded-xl bg-brand-600 px-5 py-2 text-sm font-semibold text-white hover:bg-brand-700 disabled:opacity-50 transition-colors">
                {saving ? 'Đang tạo…' : '💾 Tạo menu'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Toast ── */}
      <div className={`fixed bottom-6 right-6 z-50 flex items-center gap-2.5 rounded-xl border border-gray-200 bg-white px-4 py-3 shadow-lg transition-all duration-300 ${toastVis ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-4 pointer-events-none'}`}>
        <span className="text-green-500 text-sm">✓</span>
        <span className="text-sm text-gray-700">{toastMsg}</span>
      </div>
    </div>
  );
}
