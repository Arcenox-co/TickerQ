export type TickerStatus =
  | "Idle"
  | "Queued"
  | "InProgress"
  | "Done"
  | "DueDone"
  | "Failed"
  | "Cancelled"
  | "Skipped";

export type CronState = "Enabled" | "Disabled" | "Paused";

export interface StatusConfig {
  label: string;
  text: string;
  bg: string;
  dot: string;
  cssVar: string;
}

export const STATUS_CONFIG: Record<TickerStatus, StatusConfig> = {
  Idle:       { label: "Idle",       text: "text-status-idle",       bg: "bg-status-idle/10",       dot: "bg-status-idle",       cssVar: "var(--status-idle)" },
  Queued:     { label: "Queued",     text: "text-status-queued",     bg: "bg-status-queued/10",     dot: "bg-status-queued",     cssVar: "var(--status-queued)" },
  InProgress: { label: "Running",    text: "text-status-warning",    bg: "bg-status-warning/10",    dot: "bg-status-warning",    cssVar: "var(--status-warning)" },
  Done:       { label: "Done",       text: "text-status-healthy",    bg: "bg-status-healthy/10",    dot: "bg-status-healthy",    cssVar: "var(--status-healthy)" },
  DueDone:    { label: "DueDone",    text: "text-status-info",       bg: "bg-status-info/10",       dot: "bg-status-info",       cssVar: "var(--status-info)" },
  Failed:     { label: "Failed",     text: "text-status-error",      bg: "bg-status-error/10",      dot: "bg-status-error",      cssVar: "var(--status-error)" },
  Cancelled:  { label: "Cancelled",  text: "text-status-cancelled",  bg: "bg-status-cancelled/10",  dot: "bg-status-cancelled",  cssVar: "var(--status-cancelled)" },
  Skipped:    { label: "Skipped",    text: "text-status-skipped",    bg: "bg-status-skipped/10",    dot: "bg-status-skipped",    cssVar: "var(--status-skipped)" },
};

export function statusHsl(status: TickerStatus): string {
  return `hsl(${STATUS_CONFIG[status].cssVar})`;
}

export type HealthColor = "green" | "yellow" | "red" | "pending";

const HEALTH_MAP: Record<HealthColor, string> = {
  green: "bg-status-healthy",
  yellow: "bg-status-warning",
  red: "bg-status-error",
  pending: "bg-muted-foreground/50",
};

export function healthDotClass(status: HealthColor): string {
  return HEALTH_MAP[status] ?? "bg-muted-foreground";
}

export const CHART_AXIS_STYLE = { fontSize: 10, fill: "hsl(215 20% 55%)" };
export const CHART_GRID_STROKE = "hsl(222 30% 16%)";
export const CHART_TOOLTIP_STYLE = {
  backgroundColor: "hsl(var(--popover))",
  border: "1px solid hsl(var(--border))",
  borderRadius: "8px",
  fontSize: 11,
  color: "hsl(var(--popover-foreground))",
  boxShadow: "0 8px 24px rgba(0,0,0,0.18)",
} as const;

export const CHART_COLORS: Record<string, string> = {
  Done: statusHsl("Done"),
  Failed: statusHsl("Failed"),
  DueDone: statusHsl("DueDone"),
  Cancelled: statusHsl("Cancelled"),
  Skipped: statusHsl("Skipped"),
  Running: statusHsl("InProgress"),
  InProgress: statusHsl("InProgress"),
  Queued: statusHsl("Queued"),
  Idle: statusHsl("Idle"),
};

export function describeRetryPolicy(
  retries: number,
  parsedIntervals: number[] | null
): string {
  if (retries <= 0) return "No retries on failure.";
  if (parsedIntervals === null)
    return `${retries} retries — fix the intervals format to preview.`;
  if (parsedIntervals.length === 0)
    return `Retry ${retries}× immediately on failure.`;
  if (parsedIntervals.length === 1) {
    return `Retry every ${parsedIntervals[0]}s, up to ${retries} time${
      retries === 1 ? "" : "s"
    }.`;
  }
  const display: number[] = [];
  for (let i = 0; i < retries; i++) {
    display.push(parsedIntervals[Math.min(i, parsedIntervals.length - 1)]);
  }
  return `Retry at +${display.join("s, +")}s.`;
}

export function parseIntervalsList(raw: string): number[] | null {
  if (!raw || raw.trim().length === 0) return [];
  const parts = raw.split(",").map((s) => s.trim()).filter(Boolean);
  const out: number[] = [];
  for (const p of parts) {
    if (!/^\d+$/.test(p)) return null;
    const n = Number(p);
    if (!Number.isFinite(n) || n < 1 || n > 3600) return null;
    out.push(n);
  }
  return out;
}
