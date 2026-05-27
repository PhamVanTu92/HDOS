/**
 * DynamicIcon — renders a named icon as an inline SVG.
 * No external dependencies; uses Lucide-compatible path data.
 *
 * Usage:
 *   <DynamicIcon name="LayoutDashboard" size={20} className="text-blue-400" />
 *   <DynamicIcon name="📊" size={20} />   // emoji passthrough
 */

import type { CSSProperties, ReactNode } from 'react';

export interface IconProps {
  size?:      number;
  className?: string;
  style?:     CSSProperties;
}

// ── SVG wrapper ────────────────────────────────────────────────────────────────

function Svg({ size = 20, className, style, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size} height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      style={style}
    >
      {children}
    </svg>
  );
}

// ── Icon factory helpers ───────────────────────────────────────────────────────

type IconFn = (props: IconProps) => JSX.Element;

function p(...paths: string[]): IconFn {
  return (props: IconProps) => (
    <Svg {...props}>{paths.map((d, i) => <path key={i} d={d} />)}</Svg>
  );
}

function pc(paths: string[], circles: [number, number, number][]): IconFn {
  return (props: IconProps) => (
    <Svg {...props}>
      {paths.map((d, i) => <path key={`p${i}`} d={d} />)}
      {circles.map(([cx, cy, r], i) => <circle key={`c${i}`} cx={cx} cy={cy} r={r} />)}
    </Svg>
  );
}

function pr(paths: string[], rects: [number, number, number, number, number?][]): IconFn {
  return (props: IconProps) => (
    <Svg {...props}>
      {rects.map(([x, y, w, h, rx = 0], i) => (
        <rect key={`r${i}`} x={x} y={y} width={w} height={h} rx={rx} />
      ))}
      {paths.map((d, i) => <path key={`p${i}`} d={d} />)}
    </Svg>
  );
}

// ── Icon registry ──────────────────────────────────────────────────────────────

const ICONS: Record<string, IconFn> = {

  // Charts & data
  Activity: p('M22 12h-4l-3 9L9 3l-3 9H2'),
  AreaChart: p('M3 3v18h18', 'M3 18Q7 10 12 14T21 10'),
  BarChart:  p('M12 20V10', 'M18 20V4', 'M6 20v-6'),
  BarChart2: p('M18 20V10', 'M12 20V4', 'M6 20v-6'),
  BarChart3: p('M3 3v18h18', 'M7 16l4-8 4 4 4-8'),
  BarChart4: p('M3 20h18', 'M5 20v-8', 'M9 20V8', 'M13 20V4', 'M17 20v-8'),
  ScatterChart: pc(['M2 2L22 22', 'M2 22l20-20'], [[7, 8, 2], [17, 6, 2], [6, 17, 2], [16, 16, 2]]),
  TrendingUp:   p('M23 6L13.5 15.5 8.5 10.5 1 18', 'M17 6h6v6'),
  TrendingDown: p('M23 18L13.5 8.5 8.5 13.5 1 6', 'M17 18h6v-6'),
  PieChart: pc(['M21.21 15.89A10 10 0 118 2.83', 'M22 12A10 10 0 0012 2v10z'], []),
  Gauge: p(
    'M12 22a10 10 0 100-20 10 10 0 000 20z',
    'M12 6v6l4 2',
    'M6 12a6 6 0 006-6',
  ),
  Hash: p('M4 9h16', 'M4 15h16', 'M10 3L8 21', 'M16 3l-2 18'),

  // Layout / navigation
  LayoutDashboard: pr(
    [],
    [[3, 3, 7, 7, 1], [14, 3, 7, 7, 1], [14, 14, 7, 7, 1], [3, 14, 7, 7, 1]],
  ),
  LayoutGrid: pr(
    [],
    [[3, 3, 8, 8, 1], [13, 3, 8, 8, 1], [13, 13, 8, 8, 1], [3, 13, 8, 8, 1]],
  ),
  Grid2X2: pr(
    ['M12 3v18', 'M3 12h18'],
    [[3, 3, 18, 18, 2]],
  ),
  Grid: pr(
    ['M12 3v18', 'M3 12h18'],
    [[3, 3, 18, 18, 2]],
  ),
  Layers: p(
    'M12 2 2 7l10 5 10-5z',
    'M2 17l10 5 10-5',
    'M2 12l10 5 10-5',
  ),
  Table: pr(
    ['M3 9h18', 'M3 15h18', 'M9 3v18'],
    [[3, 3, 18, 18, 2]],
  ),
  List: p(
    'M8 6h13', 'M8 12h13', 'M8 18h13',
    'M3 6h.01', 'M3 12h.01', 'M3 18h.01',
  ),
  Menu: p('M3 12h18', 'M3 6h18', 'M3 18h18'),

  // Navigation arrows
  ArrowRight:  p('M5 12h14', 'M12 5l7 7-7 7'),
  ArrowLeft:   p('M19 12H5', 'M12 19l-7-7 7-7'),
  ChevronDown:  p('M6 9l6 6 6-6'),
  ChevronUp:    p('M18 15l-6-6-6 6'),
  ChevronLeft:  p('M15 18l-6-6 6-6'),
  ChevronRight: p('M9 18l6-6-6-6'),

  // Content / documents
  FileText: pr(
    ['M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z', 'M14 2v6h6', 'M16 13H8', 'M16 17H8', 'M10 9H8'],
    [],
  ),
  Clipboard: pr(
    ['M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2'],
    [[9, 2, 6, 4, 1]],
  ),
  ClipboardList: pr(
    ['M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2', 'M12 11h4', 'M12 16h4', 'M8 11h.01', 'M8 16h.01'],
    [[9, 2, 6, 4, 1]],
  ),
  MessageSquare: p('M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z'),
  Calendar: pr(
    ['M16 2v4', 'M8 2v4', 'M3 10h18'],
    [[3, 4, 18, 18, 2]],
  ),
  Database: p(
    'M12 2C7.58 2 4 3.34 4 5v14c0 1.66 3.58 3 8 3s8-1.34 8-3V5c0-1.66-3.58-3-8-3z',
    'M4 9c0 1.66 3.58 3 8 3s8-1.34 8-3',
    'M4 14c0 1.66 3.58 3 8 3s8-1.34 8-3',
  ),
  Save: p(
    'M19 21H5a2 2 0 01-2-2V5a2 2 0 012-2h11l5 5v11a2 2 0 01-2 2z',
    'M17 21v-8H7v8',
    'M7 3v5h8',
  ),

  // Healthcare
  Heart: p('M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z'),
  HeartPulse: p(
    'M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0016.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 002 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z',
    'M3.22 12H9.5l1.5-3 2 6 1.5-3h6.78',
  ),
  Stethoscope: p(
    'M4.8 2.3A.3.3 0 105 2H4a2 2 0 00-2 2v5a6 6 0 006 6 6 6 0 006-6V4a2 2 0 00-2-2h-.8a.3.3 0 100 .6',
    'M8 15v1a6 6 0 006 6v0a6 6 0 006-6v-4',
  ),
  BedDouble: p(
    'M2 4v16', 'M2 8h20', 'M22 4v16',
    'M2 16h20',
    'M6 8v8', 'M18 8v8',
  ),
  Microscope: p(
    'M6 18h8',
    'M3 22h18',
    'M14 22a7 7 0 000-14H4',
    'M14 8l-2 2',
    'M5 10l-2 2',
    'M9 5l2-2',
  ),
  Pill: p(
    'M10.5 20H4a2 2 0 01-2-2V6a2 2 0 012-2h3.5',
    'M20 4H10.5a2 2 0 00-2 2v12a2 2 0 002 2H20a2 2 0 002-2V6a2 2 0 00-2-2z',
    'M8 10h8',
  ),
  ShieldAlert: p(
    'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z',
    'M12 8v4',
    'M12 16h.01',
  ),
  AlertTriangle: p(
    'M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z',
    'M12 9v4',
    'M12 17h.01',
  ),
  Users: pc(
    [
      'M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2',
      'M23 21v-2a4 4 0 00-3-3.87',
      'M16 3.13a4 4 0 010 7.75',
    ],
    [[9, 7, 4]],
  ),

  // Facility / location
  Building: p(
    'M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z',
    'M9 22V12h6v10',
  ),
  Building2: p(
    'M6 22V4a2 2 0 012-2h8a2 2 0 012 2v18z',
    'M6 12H4a2 2 0 00-2 2v6a2 2 0 002 2h2',
    'M18 9h2a2 2 0 012 2v9a2 2 0 01-2 2h-2',
    'M10 6h4', 'M10 10h4', 'M10 14h4', 'M10 18h4',
  ),
  DoorOpen: p(
    'M13 4h3a2 2 0 012 2v14',
    'M2 20h3',
    'M13 20h3',
    'M13 4H3a1 1 0 00-1 1v15a1 1 0 001 1h10V4z',
    'M9 12h.01',
  ),
  MapPin: pc(
    ['M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z'],
    [[12, 10, 3]],
  ),
  Home: p(
    'M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z',
    'M9 22V12h6v10',
  ),

  // System / UI
  Settings: pc(
    [
      'M12 15a3 3 0 100-6 3 3 0 000 6z',
      'M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-2 2 2 2 0 01-2-2v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83 0 2 2 0 010-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 01-2-2 2 2 0 012-2h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 010-2.83 2 2 0 012.83 0l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 012-2 2 2 0 012 2v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 0 2 2 0 010 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 012 2 2 2 0 01-2 2h-.09a1.65 1.65 0 00-1.51 1z',
    ],
    [],
  ),
  Search: pc(
    ['M21 21l-6-6'],
    [[11, 11, 8]],
  ),
  Filter: p('M22 3H2l8 9.46V19l4 2V12.46L22 3z'),
  Sliders: p(
    'M4 21v-7', 'M4 10V3',
    'M12 21v-9', 'M12 9V3',
    'M20 21v-5', 'M20 12V3',
    'M1 14h6', 'M9 9h6', 'M17 16h6',
  ),
  Eye: pc(
    ['M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z'],
    [[12, 12, 3]],
  ),
  EyeOff: p(
    'M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94',
    'M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19',
    'M1 1l22 22',
  ),
  RefreshCw: p(
    'M23 4v6h-6',
    'M1 20v-6h6',
    'M3.51 9a9 9 0 0114.85-3.36L23 10',
    'M20.49 15a9 9 0 01-14.85 3.36L1 14',
  ),
  Plus:  p('M12 5v14', 'M5 12h14'),
  X:     p('M18 6L6 18', 'M6 6l12 12'),
  Check: p('M20 6L9 17 4 12'),
  Edit:  p('M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7', 'M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z'),
  Edit2: p('M17 3a2.828 2.828 0 114 4L7.5 20.5 2 22l1.5-5.5L17 3z'),
  Edit3: p('M12 20h9', 'M16.5 3.5a2.121 2.121 0 013 3L7 19l-4 1 1-4L16.5 3.5z'),
  Trash2: p(
    'M3 6h18',
    'M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a1 1 0 011-1h4a1 1 0 011 1v2',
    'M10 11v6',
    'M14 11v6',
  ),
  Moon: p('M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z'),
  Sun: pc(
    ['M12 8a4 4 0 100 8 4 4 0 000-8z',
     'M12 2v2', 'M12 20v2', 'M4.93 4.93l1.41 1.41',
     'M17.66 17.66l1.41 1.41', 'M2 12h2', 'M20 12h2',
     'M6.34 17.66l-1.41 1.41', 'M19.07 4.93l-1.41 1.41'],
    [],
  ),
  Terminal: p('M4 17l6-6-6-6', 'M12 19h8'),
  Database2: p(
    'M12 2C7.58 2 4 3.34 4 5s3.58 3 8 3 8-1.34 8-3-3.58-3-8-3z',
    'M4 5v3c0 1.66 3.58 3 8 3s8-1.34 8-3V5',
    'M4 8v6c0 1.66 3.58 3 8 3s8-1.34 8-3V8',
    'M4 14v3c0 1.66 3.58 3 8 3s8-1.34 8-3v-3',
  ),
  Zap: p('M13 2L3 14h9l-1 8 10-12h-9l1-8z'),
  Info: pc(['M12 16v-4', 'M12 8h.01'], [[12, 12, 10]]),
  Clock: pc(['M12 6v6l4 2'], [[12, 12, 10]]),
};

// ── Pickable icon list for icon picker UI ──────────────────────────────────────

export interface PickableIcon { name: string; category: string }

export const PICKABLE_ICONS: PickableIcon[] = [
  // Biểu đồ
  { name: 'BarChart2',      category: 'Biểu đồ' },
  { name: 'AreaChart',      category: 'Biểu đồ' },
  { name: 'PieChart',       category: 'Biểu đồ' },
  { name: 'TrendingUp',     category: 'Biểu đồ' },
  { name: 'TrendingDown',   category: 'Biểu đồ' },
  { name: 'Gauge',          category: 'Biểu đồ' },
  { name: 'ScatterChart',   category: 'Biểu đồ' },
  { name: 'Activity',       category: 'Biểu đồ' },
  { name: 'Hash',           category: 'Biểu đồ' },
  { name: 'Grid2X2',        category: 'Biểu đồ' },
  // Bố cục
  { name: 'LayoutDashboard', category: 'Bố cục' },
  { name: 'LayoutGrid',      category: 'Bố cục' },
  { name: 'Layers',          category: 'Bố cục' },
  { name: 'List',            category: 'Bố cục' },
  { name: 'Table',           category: 'Bố cục' },
  { name: 'Menu',            category: 'Bố cục' },
  // Y tế
  { name: 'HeartPulse',    category: 'Y tế' },
  { name: 'Heart',         category: 'Y tế' },
  { name: 'Stethoscope',   category: 'Y tế' },
  { name: 'BedDouble',     category: 'Y tế' },
  { name: 'Pill',          category: 'Y tế' },
  { name: 'Microscope',    category: 'Y tế' },
  { name: 'ShieldAlert',   category: 'Y tế' },
  { name: 'AlertTriangle', category: 'Y tế' },
  { name: 'Users',         category: 'Y tế' },
  { name: 'ClipboardList', category: 'Y tế' },
  // Cơ sở
  { name: 'Building2',  category: 'Cơ sở' },
  { name: 'Building',   category: 'Cơ sở' },
  { name: 'DoorOpen',   category: 'Cơ sở' },
  { name: 'MapPin',     category: 'Cơ sở' },
  { name: 'Home',       category: 'Cơ sở' },
  // Tài liệu
  { name: 'FileText',      category: 'Tài liệu' },
  { name: 'Clipboard',     category: 'Tài liệu' },
  { name: 'Calendar',      category: 'Tài liệu' },
  { name: 'Database',      category: 'Tài liệu' },
  { name: 'MessageSquare', category: 'Tài liệu' },
  // Hệ thống
  { name: 'Settings',  category: 'Hệ thống' },
  { name: 'Search',    category: 'Hệ thống' },
  { name: 'Filter',    category: 'Hệ thống' },
  { name: 'Sliders',   category: 'Hệ thống' },
  { name: 'RefreshCw', category: 'Hệ thống' },
  { name: 'Zap',       category: 'Hệ thống' },
  { name: 'Eye',       category: 'Hệ thống' },
  { name: 'Terminal',  category: 'Hệ thống' },
  { name: 'ChevronDown', category: 'Hệ thống' },
  { name: 'Clock',     category: 'Hệ thống' },
];

// ── DynamicIcon component ──────────────────────────────────────────────────────

interface DynamicIconProps extends IconProps {
  name: string;
}

export function DynamicIcon({ name, size = 20, className, style }: DynamicIconProps) {
  const IconFn = ICONS[name];
  if (IconFn) return <IconFn size={size} className={className} style={style} />;

  // Emoji pass-through (code point > 0xFF)
  if (name && name.length > 0 && (name.codePointAt(0) ?? 0) > 255) {
    return (
      <span style={{ fontSize: size * 0.9, lineHeight: 1, display: 'inline-block' }}>
        {name}
      </span>
    );
  }

  // 2-char initials fallback
  return (
    <span
      className="inline-flex items-center justify-center text-[10px] font-bold rounded"
      style={{
        width: size, height: size,
        background: 'var(--brand-dim)',
        color: 'var(--brand)',
        flexShrink: 0,
      }}
      title={name}
    >
      {name.slice(0, 2).toUpperCase()}
    </span>
  );
}
