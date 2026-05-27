/**
 * Resolves {{token}} placeholders in widget paramsTemplate JSON strings.
 * Mirrors the token reference in RENDER_CONTRACTS.md §6.3.
 *
 * Usage:
 *   const params = resolveTemplate('{"date":"{{today}}","ward":"{{filters.ward}}"}', {
 *     filters: { ward: 'ICU' },
 *     userId: 'user-123',
 *     tenantId: 'tenant-001',
 *   });
 *   // → { date: "2026-05-27", ward: "ICU" }
 */

export interface ResolveContext {
  filters?: Record<string, unknown>;
  userId?: string;
  tenantId?: string;
}

/** ISO date helpers */
function isoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

function addDays(d: Date, n: number): Date {
  const r = new Date(d);
  r.setDate(r.getDate() + n);
  return r;
}

function startOfQuarter(d: Date): Date {
  const q = Math.floor(d.getMonth() / 3);
  return new Date(d.getFullYear(), q * 3, 1);
}

function startOfYear(d: Date): Date {
  return new Date(d.getFullYear(), 0, 1);
}

/** Built-in token values (computed at call time) */
function builtinTokens(): Record<string, string> {
  const now  = new Date();
  const today = isoDate(now);

  return {
    today,
    now:               now.toISOString(),
    yesterday:         isoDate(addDays(now, -1)),
    // Month
    currentMonthStart: isoDate(startOfMonth(now)),
    currentMonth:      isoDate(startOfMonth(now)),
    currentMonthEnd:   isoDate(new Date(now.getFullYear(), now.getMonth() + 1, 0)),
    // Quarter
    currentQuarterStart: isoDate(startOfQuarter(now)),
    // Year
    currentYearStart:  isoDate(startOfYear(now)),
    // Rolling windows
    last7Days:         isoDate(addDays(now, -7)),
    last14Days:        isoDate(addDays(now, -14)),
    last30Days:        isoDate(addDays(now, -30)),
    last90Days:        isoDate(addDays(now, -90)),
    last365Days:       isoDate(addDays(now, -365)),
    // Aliases
    thisWeekStart:     isoDate(addDays(now, -(now.getDay() === 0 ? 6 : now.getDay() - 1))),
  };
}

/**
 * Replaces all `{{token}}` occurrences in `template` with resolved values.
 * Unknown tokens are replaced with JSON `null`.
 *
 * @param template - raw paramsTemplate string (JSON or plain string with tokens)
 * @param ctx      - runtime context (filters, userId, tenantId)
 * @returns        - parsed JSON object, or empty object on parse failure
 */
export function resolveTemplate(
  template: string,
  ctx: ResolveContext = {},
): Record<string, unknown> {
  if (!template || template === '{}') return {};

  const builtins = builtinTokens();

  const resolved = template.replace(/\{\{([^}]+)\}\}/g, (_, rawKey: string) => {
    const key = rawKey.trim();

    // {{filters.xxx}} or {{filters.group.subkey}} (dot-notation for nested values)
    if (key.startsWith('filters.')) {
      const parts = key.slice('filters.'.length).split('.');
      let val: unknown = ctx.filters;
      for (const part of parts) {
        if (val == null || typeof val !== 'object') { val = undefined; break; }
        val = (val as Record<string, unknown>)[part];
      }
      if (val === undefined || val === null) return 'null';
      return JSON.stringify(val);
    }

    // {{user.xxx}} or {{userId}} / {{tenantId}}
    if (key === 'userId' || key === 'user.id' || key === 'user.sub') {
      return JSON.stringify(ctx.userId ?? null);
    }
    if (key === 'tenantId' || key === 'user.tenantId') {
      return JSON.stringify(ctx.tenantId ?? null);
    }

    // Built-in date/time tokens
    if (key in builtins) {
      return JSON.stringify(builtins[key]);
    }

    // Unknown token → null
    console.warn(`[templateResolver] Unknown token: {{${key}}}`);
    return 'null';
  });

  try {
    return JSON.parse(resolved) as Record<string, unknown>;
  } catch {
    // If the entire string is not a JSON object, wrap it
    console.warn('[templateResolver] Template did not parse as JSON after resolution:', resolved);
    return {};
  }
}
