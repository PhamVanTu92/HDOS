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

// ── Gauge SVG geometry ─────────────────────────────────────────────────────────
//   viewBox 0 0 200 120, semicircle: left=(16,108) → top=(100,24) → right=(184,108)

const CX = 100;   // centre-x
const CY = 108;   // centre-y  (pushed to bottom so text fits in the bowl)
const R  = 84;    // arc radius

/**
 * Returns the SVG (x, y) for a gauge value on the semicircular arc at radius r.
 * min maps to left (angle = π), max maps to right (angle = 0).
 */
function gaugePt(v: number, min: number, max: number, r: number) {
  const t = Math.max(0, Math.min(1, (v - min) / (max - min)));
  const θ = Math.PI * (1 - t);
  return { x: CX + r * Math.cos(θ), y: CY - r * Math.sin(θ) };
}

/**
 * SVG arc-path string from gauge value `from` to `to` at radius r.
 * Always sweeps counterclockwise through the top half (sweep-flag=0).
 */
function gaugeArc(from: number, to: number, min: number, max: number, r: number): string {
  const a    = gaugePt(from, min, max, r);
  const b    = gaugePt(to,   min, max, r);
  const span = (to - from) / (max - min);
  const lg   = span > 0.5 ? 1 : 0;   // large-arc-flag
  return `M ${a.x.toFixed(1)} ${a.y.toFixed(1)} A ${r} ${r} 0 ${lg} 0 ${b.x.toFixed(1)} ${b.y.toFixed(1)}`;
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

  // Clamp value to [min, max] for rendering (needle + fill), but display raw value
  const clamped = Math.max(min, Math.min(max, value));

  // Active threshold: the band whose `from` is closest to (and ≤) clamped value
  const active =
    [...thresholds].reverse().find((th) => clamped >= th.from) ??
    thresholds[0] ??
    null;

  const activeColor = active ? toColor(active.color) : 'var(--brand)';
  const activeBg    = active ? toBg(active.color)    : 'transparent';

  // Needle tip sits at R-14 so it doesn't reach the arc track
  const needle = gaugePt(clamped, min, max, R - 14);

  // Format display value: drop decimals if integer
  const displayVal = value % 1 === 0 ? String(value) : value.toFixed(1);

  // Target tick mark: straddles the arc track
  const targetOuter = target != null ? gaugePt(target, min, max, R + 6)  : null;
  const targetInner = target != null ? gaugePt(target, min, max, R - 6)  : null;

  return (
    <div className="flex flex-col items-center justify-center h-full gap-1.5">
      {/* ── SVG gauge ──────────────────────────────────────────────────── */}
      <svg viewBox="0 0 200 120" className="w-full max-w-[280px]" aria-label="gauge">

        {/* Gray track — full semicircle */}
        <path
          d={gaugeArc(min, max, min, max, R)}
          fill="none"
          stroke="var(--border)"
          strokeWidth="14"
          strokeLinecap="round"
        />

        {/* Colored fill — threshold bands clipped to [min, clamped] */}
        {thresholds.map((th, i) => {
          const segFrom = Math.max(th.from, min);
          const segTo   = Math.min(th.to,   clamped);
          if (segFrom >= segTo) return null;
          return (
            <path
              key={i}
              d={gaugeArc(segFrom, segTo, min, max, R)}
              fill="none"
              stroke={toColor(th.color)}
              strokeWidth="10"
              strokeLinecap="round"
            />
          );
        })}

        {/* Target tick — perpendicular mark across the arc track */}
        {targetOuter && targetInner && (
          <line
            x1={targetInner.x.toFixed(1)} y1={targetInner.y.toFixed(1)}
            x2={targetOuter.x.toFixed(1)} y2={targetOuter.y.toFixed(1)}
            stroke="var(--tx)"
            strokeWidth="2.5"
            strokeLinecap="round"
          />
        )}

        {/* Needle — thin line from centre to arc */}
        <line
          x1={CX} y1={CY}
          x2={needle.x.toFixed(1)} y2={needle.y.toFixed(1)}
          stroke="var(--tx)"
          strokeWidth="2"
          strokeLinecap="round"
        />
        {/* Centre pivot */}
        <circle cx={CX} cy={CY} r={5.5} fill="var(--tx)" />
        <circle cx={CX} cy={CY} r={2.5} fill="var(--surface)" />

        {/* Min / Max corner labels */}
        <text x="14"  y="119" textAnchor="middle" fontSize="8" fill="var(--tx3)">
          {min}{unit}
        </text>
        <text x="186" y="119" textAnchor="middle" fontSize="8" fill="var(--tx3)">
          {max}{unit}
        </text>

        {/* Big value number */}
        <text
          x={CX} y="78"
          textAnchor="middle"
          fontSize="27"
          fontWeight="700"
          fill={activeColor}
        >
          {displayVal}
        </text>

        {/* Unit below value */}
        {unit && (
          <text x={CX} y="93" textAnchor="middle" fontSize="11" fill="var(--tx2)">
            {unit}
          </text>
        )}
      </svg>

      {/* ── Status badge ───────────────────────────────────────────────── */}
      {active && (
        <span
          className="text-xs font-medium px-2.5 py-0.5 rounded-full"
          style={{ color: activeColor, background: activeBg }}
        >
          {active.label}
        </span>
      )}

      {/* ── Target caption ─────────────────────────────────────────────── */}
      {target != null && (
        <p className="text-[10px] text-[--tx3]">
          Mục tiêu: {target}{unit}
        </p>
      )}
    </div>
  );
}
