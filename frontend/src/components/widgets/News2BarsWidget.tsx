interface News2Parameter {
  name:  string;
  code?: string;
  value: number;
  unit?: string;
  score: number;
  min?:  number;
  max?:  number;
}

interface News2Data {
  patientId?:   string;
  patientName?: string;
  totalScore:   number;
  riskLevel?:   'low' | 'medium' | 'high' | 'critical' | string;
  parameters:   News2Parameter[];
  assessedAt?:  string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function scoreColor(score: number): string {
  if (score === 0) return 'var(--tx3)';
  if (score === 1) return 'var(--info)';
  if (score === 2) return 'var(--warning)';
  return 'var(--danger)';
}

function scoreBg(score: number): string {
  if (score === 0) return 'rgba(255,255,255,0.05)';
  if (score === 1) return 'var(--info-bg)';
  if (score === 2) return 'var(--warning-bg)';
  return 'var(--danger-bg)';
}

const RISK_LABEL: Record<string, string> = {
  low:      'Thấp',
  medium:   'Trung bình',
  high:     'Cao',
  critical: 'Nguy kịch',
};

const RISK_BADGE_COLOR: Record<string, string> = {
  low:      'var(--success)',
  medium:   'var(--warning)',
  high:     'var(--danger)',
  critical: 'var(--danger)',
};

const RISK_BADGE_BG: Record<string, string> = {
  low:      'var(--success-bg)',
  medium:   'var(--warning-bg)',
  high:     'var(--danger-bg)',
  critical: 'var(--danger-bg)',
};

function formatAssessedAt(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  const mo = String(d.getMonth() + 1).padStart(2, '0');
  const yy = d.getFullYear();
  return `${hh}:${mm} ${dd}/${mo}/${yy}`;
}

function barFill(param: News2Parameter): number {
  if (param.min != null && param.max != null && param.max !== param.min) {
    const pct = (param.value - param.min) / (param.max - param.min);
    return Math.min(100, Math.max(0, pct * 100));
  }
  // Fallback: score/3 as simple score bar
  return Math.min(100, (param.score / 3) * 100);
}

// ── Sub-components ────────────────────────────────────────────────────────────

interface ScoreBadgeProps {
  score: number;
}

function ScoreBadge({ score }: ScoreBadgeProps) {
  return (
    <span
      className="inline-flex items-center justify-center w-5 h-5 rounded text-[10px] font-bold shrink-0 tabular-nums"
      style={{ color: scoreColor(score), background: scoreBg(score) }}
    >
      {score}
    </span>
  );
}

interface RiskBadgeProps {
  riskLevel: string;
  totalScore: number;
}

function RiskBadge({ riskLevel, totalScore }: RiskBadgeProps) {
  const label = RISK_LABEL[riskLevel] ?? riskLevel;
  const color = RISK_BADGE_COLOR[riskLevel] ?? 'var(--info)';
  const bg    = RISK_BADGE_BG[riskLevel]    ?? 'var(--info-bg)';
  const isCritical = riskLevel === 'critical';

  return (
    <div className="flex items-center gap-2">
      <span
        className="text-3xl tabular-nums"
        style={{ color, fontWeight: 700, lineHeight: 1 }}
      >
        {totalScore}
      </span>
      <span
        className={`px-2 py-0.5 rounded text-xs ${isCritical ? 'font-bold' : 'font-medium'}`}
        style={{ color, background: bg }}
      >
        {label.toUpperCase()}
      </span>
    </div>
  );
}

interface ParameterRowProps {
  param: News2Parameter;
}

function ParameterRow({ param }: ParameterRowProps) {
  const fill  = barFill(param);
  const color = scoreColor(param.score);
  const valueText = `${param.value}${param.unit ? ` ${param.unit}` : ''}`;

  return (
    <div className="flex items-center gap-2 py-1">
      {/* Name */}
      <span
        className="text-xs text-[--tx2] shrink-0 truncate"
        style={{ width: '120px' }}
        title={param.name}
      >
        {param.name}
      </span>

      {/* Value + unit */}
      <span
        className="text-xs font-mono text-[--tx] shrink-0 text-right"
        style={{ width: '80px' }}
      >
        {valueText}
      </span>

      {/* Score badge */}
      <ScoreBadge score={param.score} />

      {/* Progress bar */}
      <div
        className="flex-1 h-2 rounded-full overflow-hidden"
        style={{ background: 'var(--border)' }}
      >
        <div
          className="h-full rounded-full transition-all"
          style={{ width: `${fill}%`, background: color }}
        />
      </div>
    </div>
  );
}

// ── Main widget ───────────────────────────────────────────────────────────────

export function News2BarsWidget({ data }: { data: unknown }) {
  const d = data as News2Data | null;

  if (!d || !Array.isArray(d.parameters) || d.parameters.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-[--tx3] text-sm">
        Chưa có dữ liệu đánh giá NEWS2
      </div>
    );
  }

  const riskLevel = d.riskLevel ?? 'low';

  return (
    <div className="flex flex-col h-full overflow-y-auto gap-3">
      {/* Header */}
      <div
        className="flex items-center justify-between gap-3 pb-2"
        style={{ borderBottom: '1px solid var(--border)' }}
      >
        <div className="flex flex-col gap-0.5 min-w-0">
          {d.patientName && (
            <p className="text-xs text-[--tx2] truncate">
              Bệnh nhân: <span className="font-medium text-[--tx]">{d.patientName}</span>
            </p>
          )}
          {d.assessedAt && (
            <p className="text-[10px] text-[--tx3]">
              Đánh giá lúc: {formatAssessedAt(d.assessedAt)}
            </p>
          )}
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <span className="text-xs text-[--tx2]">Tổng điểm:</span>
          <RiskBadge riskLevel={riskLevel} totalScore={d.totalScore} />
        </div>
      </div>

      {/* Parameter rows */}
      <div className="flex flex-col divide-y" style={{ '--divide-color': 'var(--border)' } as React.CSSProperties}>
        {d.parameters.map((param, idx) => (
          <ParameterRow key={param.code ?? `${param.name}-${idx}`} param={param} />
        ))}
      </div>
    </div>
  );
}
