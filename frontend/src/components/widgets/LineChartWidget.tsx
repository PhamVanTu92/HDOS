import {
  ResponsiveContainer, LineChart, BarChart, AreaChart,
  Line, Bar, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend,
} from 'recharts';
import type { WidgetChartType } from '../../types/module';

interface SeriesPoint { x: string | number; y: number | null }
interface ChartSeries { name: string; data: SeriesPoint[] }
interface ChartData {
  series?: ChartSeries[];
  axes?: { x?: { label?: string; format?: string }; y?: { label?: string; format?: string } };
}

const COLORS = ['#1a73d4','#2ecc71','#f0a030','#ff5252','#a29bfe','#00cec9','#4da3ff'];

function buildRechartsData(series: ChartSeries[]): Record<string, unknown>[] {
  if (!series.length) return [];
  const allX = Array.from(new Set(series.flatMap(s => s.data.map(p => String(p.x)))));
  return allX.map(x => {
    const row: Record<string, unknown> = { x };
    series.forEach(s => {
      const pt = s.data.find(p => String(p.x) === x);
      row[s.name] = pt?.y ?? null;
    });
    return row;
  });
}

export function LineChartWidget({ data, chartType }: { data: unknown; chartType?: WidgetChartType }) {
  const d = data as ChartData | null;
  if (!d?.series?.length) return <div className="flex items-center justify-center h-full text-[--tx3] text-sm">Không có dữ liệu</div>;

  const chartData = buildRechartsData(d.series);
  const type = chartType ?? 'line_chart';

  const commonProps = {
    data: chartData,
    margin: { top: 4, right: 8, bottom: 0, left: 0 },
  };

  const axisStyle = { fill: 'var(--tx3)', fontSize: 10 };
  const gridStyle = { stroke: 'rgba(255,255,255,0.05)' };

  return (
    <ResponsiveContainer width="100%" height="100%">
      {type === 'bar_chart' ? (
        <BarChart {...commonProps}>
          <CartesianGrid strokeDasharray="3 3" {...gridStyle} />
          <XAxis dataKey="x" tick={axisStyle} axisLine={false} tickLine={false} />
          <YAxis tick={axisStyle} axisLine={false} tickLine={false} width={35} />
          <Tooltip
            contentStyle={{ background: 'var(--overlay)', border: '1px solid var(--border)', borderRadius: 8, color: 'var(--tx)', fontSize: 12 }}
            cursor={{ fill: 'rgba(255,255,255,0.05)' }}
          />
          {d.series.length > 1 && <Legend wrapperStyle={{ fontSize: 11, color: 'var(--tx2)' }} />}
          {d.series.map((s, i) => (
            <Bar key={s.name} dataKey={s.name} fill={COLORS[i % COLORS.length]} radius={[3, 3, 0, 0]} maxBarSize={40} />
          ))}
        </BarChart>
      ) : type === 'area_chart' ? (
        <AreaChart {...commonProps}>
          <defs>
            {d.series.map((s, i) => (
              <linearGradient key={s.name} id={`ag-${i}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor={COLORS[i % COLORS.length]} stopOpacity={0.3} />
                <stop offset="95%" stopColor={COLORS[i % COLORS.length]} stopOpacity={0} />
              </linearGradient>
            ))}
          </defs>
          <CartesianGrid strokeDasharray="3 3" {...gridStyle} />
          <XAxis dataKey="x" tick={axisStyle} axisLine={false} tickLine={false} />
          <YAxis tick={axisStyle} axisLine={false} tickLine={false} width={35} />
          <Tooltip contentStyle={{ background: 'var(--overlay)', border: '1px solid var(--border)', borderRadius: 8, color: 'var(--tx)', fontSize: 12 }} />
          {d.series.length > 1 && <Legend wrapperStyle={{ fontSize: 11, color: 'var(--tx2)' }} />}
          {d.series.map((s, i) => (
            <Area key={s.name} type="monotone" dataKey={s.name} stroke={COLORS[i % COLORS.length]}
              strokeWidth={2} fill={`url(#ag-${i})`} connectNulls dot={false} />
          ))}
        </AreaChart>
      ) : (
        // line_chart (default)
        <LineChart {...commonProps}>
          <CartesianGrid strokeDasharray="3 3" {...gridStyle} />
          <XAxis dataKey="x" tick={axisStyle} axisLine={false} tickLine={false} />
          <YAxis tick={axisStyle} axisLine={false} tickLine={false} width={35} />
          <Tooltip contentStyle={{ background: 'var(--overlay)', border: '1px solid var(--border)', borderRadius: 8, color: 'var(--tx)', fontSize: 12 }} />
          {d.series.length > 1 && <Legend wrapperStyle={{ fontSize: 11, color: 'var(--tx2)' }} />}
          {d.series.map((s, i) => (
            <Line key={s.name} type="monotone" dataKey={s.name} stroke={COLORS[i % COLORS.length]}
              strokeWidth={2} dot={false} connectNulls />
          ))}
        </LineChart>
      )}
    </ResponsiveContainer>
  );
}
