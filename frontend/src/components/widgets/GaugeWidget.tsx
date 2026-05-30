// ── Types ──────────────────────────────────────────────────────────────────────

interface Threshold {
  from:  number;
  to:    number;
  color: string;
  label: string;
}

interface GaugeData {
  value:      number;
  min:        number;
  max:        number;
  unit?:      string;
  thresholds: Threshold[];
  target?:    number;
  label?:     string;
}

// ── Color maps ─────────────────────────────────────────────────────────────────

const COLOR_VAR: Record<string, string> = {
  danger:  'var(--danger)',
  warning: 'var(--warning)',
  success: 'var(--success)',
  info:    'var(--info)',
};
const BG_VAR: Record<string, string> = {
  danger:  'var(--danger-bg)',
  warning: 'var(--warning-bg)',
  success: 'var(--success-bg)',
  info:    'var(--info-bg)',
};

const toColor = (c: string) => COLOR_VAR[c] ?? c;
const toBg    = (c: string) => BG_VAR[c]    ?? 'transparent';

// ── Gauge geometry ─────────────────────────────────────────────────────────────
//   viewBox 0 0 220 130
//   Semicircle: left=(22,115) → top=(110,27) → right=(198,115)

const CX = 110;
const CY = 115;
const R  = 88;

/** SVG point on the semicircular arc at radius r for a given value. */
function gaugePt(v: number, min: number, max: number, r: number) {
  const t = Math.max(0, Math.min(1, (v - min) / (max - min)));
  const θ = Math.PI * (1 - t);   // π at min (left) → 0 at max (right)
  return { x: CX + r * Math.cos(θ), y: CY - r * Math.sin(θ), θ };
}

/** SVG arc-path string from value `from` to `to` (sweep counterclockwise through top). */
function gaugeArc(from: number, to: number, min: number, max: number, r: number): string {
  const a  = gaugePt(from, min, max, r);
  const b  = gaugePt(to,   min, max, r);
  const lg = (to - from) / (max - min) > 0.5 ? 1 : 0;
  return `M ${a.x.toFixed(2)} ${a.y.toFixed(2)} A ${r} ${r} 0 ${lg} 0 ${b.x.toFixed(2)} ${b.y.toFixed(2)}`;
}

/** Filled triangle needle polygon: tip at radius tipR, base half-width hw. */
function needlePoints(v: number, min: number, max: number, tipR: number, hw: number): string {
  const { θ } = gaugePt(v, min, max, tipR);
  const tipX  = CX + tipR * Math.cos(θ);
  const tipY  = CY - tipR * Math.sin(θ);
  const bx1   = CX + hw * Math.cos(θ + Math.PI / 2);
  const by1   = CY - hw * Math.sin(θ + Math.PI / 2);
  const bx2   = CX + hw * Math.cos(θ - Math.PI / 2);
  const by2   = CY - hw * Math.sin(θ - Math.PI / 2);
  return `${tipX.toFixed(2)},${tipY.toFixed(2)} ${bx1.toFixed(2)},${by1.toFixed(2)} ${bx2.toFixed(2)},${by2.toFixed(2)}`;
}

/** SVG textAnchor for a label placed outside the arc at this value position. */
function labelAnchor(v: number, min: number, max: number): string {
  const cosθ = Math.cos(gaugePt(v, min, max, R).θ);
  if (cosθ < -0.35) return 'start';   // left half → text extends right
  if (cosθ >  0.35) return 'end';     // right half → text extends left
  return 'middle';
}

/** Compact label: integers without decimal, decimals up to 2 places, no trailing zeros. */
function fmtLabel(v: number): string {
  return Number.isInteger(v) ? String(v) : parseFloat(v.toFixed(2)).toString();
}

// ── Widget ─────────────────────────────────────────────────────────────────────

export function GaugeWidget({ data }: { data: unknown }) {
  const d = data as GaugeData | null;

  if (!d || d.min == null || d.max == null || d.max === d.min) {
    return (
      <div className="flex items-center justify-center h-full text-[--tx3] text-sm">
        Không có dữ liệu
      </div>
    );
  }

  const { value, min, max, unit = '', thresholds = [], target } = d;
  const clamped = Math.max(min, Math.min(max, value));

  // Active threshold for the status badge
  const active =
    [...thresholds].reverse().find((th) => clamped >= th.from) ??
    thresholds[0] ??
    null;

  const activeColor = active ? toColor(active.color) : 'var(--brand)';
  const activeBg    = active ? toBg(active.color)    : 'transparent';

  const displayVal  = value % 1 === 0 ? String(value) : value.toFixed(1);

  // All unique threshold boundary values for axis labels
  const boundaries = [
    ...new Set([
      min,
      ...thresholds.flatMap((th) => [th.from, th.to]),
      max,
    ]),
  ]
    .filter((v) => v >= min && v <= max)
    .sort((a, b) => a - b);

  // Overlap between adjacent bands: next band starts ε before its boundary so
  // it paints over the previous band's butt edge — no junction gap, no blobs.
  const ε = (max - min) * 0.005;

  // Target tick
  const tgtOuter = target != null ? gaugePt(target, min, max, R + 8)  : null;
  const tgtInner = target != null ? gaugePt(target, min, max, R - 4)  : null;

  return (
    <div className="flex flex-col items-center justify-center h-full gap-1.5">
      <div className="w-full max-w-[440px]">
        <svg viewBox="0 0 220 130" className="w-full" aria-label="gauge">

          {/* ── Gray track ─────────────────────────────────────────────── */}
          <path
            d={gaugeArc(min, max, min, max, R)}
            fill="none"
            stroke="var(--border)"
            strokeWidth="16"
            strokeLinecap="round"
          />

          {/* ── Threshold bands (ALL always visible, full opacity) ──────── */}
          {thresholds.map((th, i) => {
            // Each non-first band extends ε into the previous band's territory
            // so its butt edge covers the previous band's butt edge cleanly.
            const from = Math.max(i === 0 ? th.from : th.from - ε, min);
            const to   = Math.min(th.to, max);
            if (from >= to) return null;
            return (
              <path
                key={i}
                d={gaugeArc(from, to, min, max, R)}
                fill="none"
                stroke={toColor(th.color)}
                strokeWidth="12"
                strokeLinecap="butt"
              />
            );
          })}

          {/* ── Target tick ─────────────────────────────────────────────── */}
          {tgtOuter && tgtInner && (
            <line
              x1={tgtInner.x.toFixed(2)} y1={tgtInner.y.toFixed(2)}
              x2={tgtOuter.x.toFixed(2)} y2={tgtOuter.y.toFixed(2)}
              stroke="var(--tx)"
              strokeWidth="2"
              strokeLinecap="round"
            />
          )}

          {/* ── Boundary value labels ───────────────────────────────────── */}
          {boundaries.map((bv, i) => {
            const LABEL_R = R + 14;
            const pt  = gaugePt(bv, min, max, LABEL_R);
            const anc = labelAnchor(bv, min, max);
            return (
              <text
                key={i}
                x={pt.x.toFixed(2)}
                y={pt.y.toFixed(2)}
                textAnchor={anc}
                dominantBaseline="central"
                fontSize="8"
                fill="var(--tx3)"
              >
                {fmtLabel(bv)}
              </text>
            );
          })}

          {/* ── Needle (filled triangle) ────────────────────────────────── */}
          <polygon
            points={needlePoints(clamped, min, max, R - 3, 5)}
            fill="var(--tx)"
          />

          {/* ── Centre pivot ────────────────────────────────────────────── */}
          <circle cx={CX} cy={CY} r={7}   fill="var(--tx)" />
          <circle cx={CX} cy={CY} r={3.5} fill="var(--surface)" />

          {/* ── Value number ────────────────────────────────────────────── */}
          <text
            x={CX} y={CY - 38}
            textAnchor="middle"
            dominantBaseline="central"
            fontSize="26"
            fontWeight="700"
            fill={activeColor}
          >
            {displayVal}
          </text>

          {/* ── Unit ────────────────────────────────────────────────────── */}
          {unit && (
            <text
              x={CX} y={CY - 16}
              textAnchor="middle"
              dominantBaseline="central"
              fontSize="11"
              fill="var(--tx2)"
            >
              {unit}
            </text>
          )}
        </svg>
      </div>

      {/* ── Status badge ─────────────────────────────────────────────────── */}
      {active && (
        <span
          className="text-xs font-medium px-2.5 py-0.5 rounded-full"
          style={{ color: activeColor, background: activeBg }}
        >
          {active.label}
        </span>
      )}

      {/* ── Target caption ───────────────────────────────────────────────── */}
      {target != null && (
        <p className="text-[10px] text-[--tx3]">
          Mục tiêu: {target}{unit}
        </p>
      )}
    </div>
  );
}
