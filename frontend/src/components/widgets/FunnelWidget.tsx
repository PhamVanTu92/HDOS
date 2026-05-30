// ── Types ──────────────────────────────────────────────────────────────────────

interface FunnelStep {
  label:          string;
  value:          number;
  percentOfStart: number;
  dropRate:       number | null;
}

interface FunnelData {
  steps: FunnelStep[];
}

// ── Widget ─────────────────────────────────────────────────────────────────────

export function FunnelWidget({ data }: { data: unknown }) {
  const d = data as FunnelData | null;

  if (!d?.steps?.length) {
    return (
      <div className="flex items-center justify-center h-full text-[--tx3] text-sm">
        Không có dữ liệu
      </div>
    );
  }

  const { steps } = d;

  return (
    <div className="flex flex-col h-full overflow-y-auto gap-0.5 py-1">
      {steps.map((step, i) => {
        // Bar opacity fades gently so the shrinking shape reads as a funnel
        const opacity = 1 - (i / Math.max(steps.length - 1, 1)) * 0.45;

        return (
          <div key={i}>
            {/* ── Drop-rate badge between steps ──────────────────────── */}
            {i > 0 && step.dropRate != null && (
              <div className="flex items-center justify-center py-0.5 gap-2">
                <div
                  className="flex-1 h-px"
                  style={{ background: 'var(--border)', maxWidth: '48px' }}
                />
                <span
                  className="text-[10px] font-medium tabular-nums px-1.5 py-px rounded"
                  style={{ color: 'var(--danger)', background: 'var(--danger-bg)' }}
                >
                  ↓ {step.dropRate.toFixed(1)}%
                </span>
                <div
                  className="flex-1 h-px"
                  style={{ background: 'var(--border)', maxWidth: '48px' }}
                />
              </div>
            )}

            {/* ── Step row ────────────────────────────────────────────── */}
            <div className="flex items-center gap-2 min-w-0">

              {/* Fixed-width label — always readable, tooltip on hover */}
              <p
                className="text-xs shrink-0 truncate leading-tight"
                style={{ width: '128px', color: 'var(--tx2)' }}
                title={step.label}
              >
                {step.label}
              </p>

              {/* Bar track + fill */}
              <div
                className="flex-1 rounded overflow-hidden"
                style={{ height: '30px', background: 'var(--border)' }}
              >
                <div
                  className="h-full rounded"
                  style={{
                    width: `${step.percentOfStart}%`,
                    background: 'var(--brand)',
                    opacity,
                    transition: 'width 0.4s ease',
                  }}
                />
              </div>

              {/* Value + percent (right column) */}
              <div className="shrink-0 text-right" style={{ width: '64px' }}>
                <p
                  className="text-xs font-bold tabular-nums leading-tight"
                  style={{ color: 'var(--tx)' }}
                >
                  {step.value.toLocaleString('vi-VN')}
                </p>
                <p
                  className="text-[10px] tabular-nums leading-tight"
                  style={{ color: 'var(--tx3)' }}
                >
                  {step.percentOfStart.toFixed(1)}%
                </p>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
