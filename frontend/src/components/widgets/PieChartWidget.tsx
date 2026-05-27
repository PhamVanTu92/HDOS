import { ResponsiveContainer, PieChart, Pie, Cell, Tooltip, Legend } from 'recharts';
import type { WidgetChartType } from '../../types/module';

interface PieSlice { label: string; value: number; color?: string | null }

const COLORS = ['#1a73d4','#2ecc71','#f0a030','#ff5252','#a29bfe','#00cec9','#4da3ff','#fd79a8'];

export function PieChartWidget({ data, chartType }: { data: unknown; chartType?: WidgetChartType }) {
  // Support both {slices:[]} and {rows:[{label,value}]}
  type Row = { label?: string; value?: number; name?: string };
  const rawData = data as { slices?: PieSlice[]; rows?: Row[] } | null;
  const slices: PieSlice[] = rawData?.slices ?? rawData?.rows?.map(r => ({
    label: r.label ?? r.name ?? '?',
    value: r.value ?? 0,
  })) ?? [];

  if (!slices.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  const isDonut = chartType === 'donut_chart';

  return (
    <ResponsiveContainer width="100%" height="100%">
      <PieChart>
        <Pie
          data={slices}
          dataKey="value"
          nameKey="label"
          cx="50%"
          cy="50%"
          innerRadius={isDonut ? '55%' : 0}
          outerRadius="75%"
          paddingAngle={2}
        >
          {slices.map((slice, i) => (
            <Cell key={slice.label} fill={slice.color ?? COLORS[i % COLORS.length]} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={{ background: 'var(--overlay)', border: '1px solid var(--border)', borderRadius: 8, color: 'var(--tx)', fontSize: 12 }}
          formatter={(val: number) => val.toLocaleString('vi-VN')}
        />
        <Legend
          formatter={(value) => <span style={{ color: 'var(--tx2)', fontSize: 11 }}>{value}</span>}
          iconSize={8}
        />
      </PieChart>
    </ResponsiveContainer>
  );
}
