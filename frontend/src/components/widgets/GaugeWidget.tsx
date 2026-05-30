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
//   viewBox 0 0 200 120
//   Semicircle endpoints: left=(10,108) top=(100,18) right=(190,108)
//   CY is pushed toward the bottom so the text fits inside the arc bowl.

const CX = 100;
const CY = 108;
const R  = 90;    // larger radius fills the viewBox better

/**
 * Returns the SVG point for a gauge value on the semicircle at radius r.
 * min → left (θ = π)   max → right (θ = 0)
 */
function gaugePt(v: number, min: number, max: number, r: number) {
  const t = Math.max(0, Math.min(1, (v - min) / (max - min)));
  const θ = Math.PI * (1 - t);
  return { x: CX + r * Math.cos(θ), y: CY - r * Math.sin(θ) };
}

/**
 * SVG arc-path string from gauge value `from` to `to` at radius r.
 * Sweeps counterclockwise through the top half (sweep-flag = 0).
 */
function gaugeArc(from: number, to: number, min: number, max: number, r: number): string {
  const a  = gaugePt(from, min, max, r);
  const b  = gaugePt(to,   min, max, r);
  const lg = (to - from) / (max - min) > 0.5 ? 1 : 0;
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
  const clamped = Math.max(min, Math.min(max, value));

  // Active threshold: the band whose `from` ≤ clamped value (search from top)
  const active =
    [...thresholds].reverse().find((th) => clamped >= th.from) ??
    thresholds[0] ??
    null;

  const activeColor = active ? toColor(active.color) : 'var(--brand)';
  const activeBg    = active ? toBg(active.color)    : 'transparent';

  const needle     = gaugePt(clamped, min, max, R - 18);
  const displayVal = value % 1 === 0 ? String(value) : value.toFixed(1);

  const targetOuter = target != null ? gaugePt(target, min, max, R + 7) : null;
  const targetInner = target != null ? gaugePt(target, min, max, R - 7) : null;

  // Small overlap between adjacent segments so the next segment's butt cap
  // cleanly covers the previous one's edge — avoids junction gap artefacts
  // without the semicircular blobs that strokeLinecap="round" creates.
  const ε = (max - min) * 0.01;

  return (
    <div className="flex flex-col items-center justify-center h-full gap-2">

      {/* SVG fills container width up to 420 px; height auto from viewBox ratio */}
      <div className="w-full max-w-[420px]">
        <svg viewBox="0 0 200 120" className="w-full" aria-label="gauge">

          {/* ── Gray track (full range, widest, painted first) ──────────── */}
          <path
            d={gaugeArc(min, max, min, max, R)}
            fill="none"
            stroke="var(--border)"
            strokeWidth="16"
            strokeLinecap="round"
          />

          {/* ── Threshold fill segments ─────────────────────────────────────
               Strategy: butt caps on all segments; each non-first segment
               starts ε before its threshold boundary so it paints over the
               previous segment's flat edge — clean junctions, no round blobs.
               First segment uses round so the gauge's left end looks finished. */}
          {thresholds.map((th, i) => {
            // Extend left edge slightly into previous segment (hidden by overlap)
            const segFrom = Math.max(i === 0 ? th.from : th.from - ε, min);
            const segTo   = Math.min(th.to, clamped);
            if (segFrom >= segTo) return null;
            return (
              <path
                key={i}
                d={gaugeArc(segFrom, segTo, min, max, R)}
                fill="none"
                stroke={toColor(th.color)}
                strokeWidth="12"
                strokeLinecap={i === 0 ? 'round' : 'butt'}
              />
            );
          })}

          {/* ── Target tick (straddles the arc track) ───────────────────── */}
          {targetOuter && targetInner && (
            <line
              x1={targetInner.x.toFixed(1)} y1={targetInner.y.toFixed(1)}
              x2={targetOuter.x.toFixed(1)} y2={targetOuter.y.toFixed(1)}
              stroke="var(--tx)"
              strokeWidth="2.5"
              strokeLinecap="round"
            />
          )}

          {/* ── Needle ──────────────────────────────────────────────────── */}
          <line
            x1={CX} y1={CY}
            x2={needle.x.toFixed(1)} y2={needle.y.toFixed(1)}
            stroke="var(--tx)"
            strokeWidth="2.5"
            strokeLinecap="round"
          />
          <circle cx={CX} cy={CY} r={6}   fill="var(--tx)" />
          <circle cx={CX} cy={CY} r={2.5} fill="var(--surface)" />

          {/* ── Min / Max labels ────────────────────────────────────────── */}
          <text x="9"   y="118" textAnchor="middle" fontSize="8" fill="var(--tx3)">
            {min}{unit}
          </text>
          <text x="191" y="118" textAnchor="middle" fontSize="8" fill="var(--tx3)">
            {max}{unit}
          </text>

          {/* ── Value (large) ───────────────────────────────────────────── */}
          <text
            x={CX} y="74"
            textAnchor="middle"
            fontSize="28"
            fontWeight="700"
            fill={activeColor}
          >
            {displayVal}
          </text>

          {/* ── Unit ────────────────────────────────────────────────────── */}
          {unit && (
            <text x={CX} y="90" textAnchor="middle" fontSize="12" fill="var(--tx2)">
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
