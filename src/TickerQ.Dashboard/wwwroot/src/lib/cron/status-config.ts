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

/**
 * Collapses every job status in one parallel branch into the status displayed
 * for that branch at the synchronization footer. Conditional descendants that
 * did not run are Skipped, so Skipped is deliberately the weakest signal: a
 * completed branch with an unused failure/cancellation path still reads Done.
 * A live descendant takes precedence over a recorded terminal result because
 * that branch has not reached the synchronization point yet.
 */
export function aggregateBranchSubtreeStatus(
  statuses: readonly TickerStatus[]
): TickerStatus {
  if (statuses.length === 0) return "Idle";

  const present = new Set(statuses);
  const precedence: readonly TickerStatus[] = [
    "InProgress",
    "Queued",
    "Idle",
    "Failed",
    "Cancelled",
    "Done",
    "DueDone",
    "Skipped",
  ];

  return precedence.find(status => present.has(status)) ?? "Idle";
}

const TERMINAL_TICKER_STATUSES = new Set<TickerStatus>([
  "Done",
  "DueDone",
  "Failed",
  "Cancelled",
  "Skipped",
]);

/** True only after every persisted job in the branch has reached a final state. */
export function isBranchSubtreeSettled(statuses: readonly TickerStatus[]): boolean {
  return statuses.length > 0 &&
    statuses.every(status => TERMINAL_TICKER_STATUSES.has(status));
}

export interface BranchStatusSummary {
  key: "done" | "failed" | "running" | "cancelled" | "skipped" | "queued" | "idle";
  label: string;
  count: number;
  color: string;
}

/**
 * Produces the compact status breakdown shown at a parallel join. Done and
 * DueDone intentionally share one user-facing bucket because both mean that
 * the branch completed successfully.
 */
export function summarizeBranchStatuses(
  statuses: readonly TickerStatus[]
): BranchStatusSummary[] {
  const counts = new Map<BranchStatusSummary["key"], number>();
  const increment = (key: BranchStatusSummary["key"]) =>
    counts.set(key, (counts.get(key) ?? 0) + 1);

  for (const status of statuses) {
    switch (status) {
      case "Done":
      case "DueDone":
        increment("done");
        break;
      case "Failed":
        increment("failed");
        break;
      case "InProgress":
        increment("running");
        break;
      case "Cancelled":
        increment("cancelled");
        break;
      case "Skipped":
        increment("skipped");
        break;
      case "Queued":
        increment("queued");
        break;
      case "Idle":
        increment("idle");
        break;
    }
  }

  const definitions: Array<{
    key: BranchStatusSummary["key"];
    label: string;
    status: TickerStatus;
  }> = [
    { key: "done", label: "done", status: "Done" },
    { key: "failed", label: "failed", status: "Failed" },
    { key: "running", label: "running", status: "InProgress" },
    { key: "cancelled", label: "cancelled", status: "Cancelled" },
    { key: "skipped", label: "skipped", status: "Skipped" },
    { key: "queued", label: "queued", status: "Queued" },
    { key: "idle", label: "idle", status: "Idle" },
  ];

  return definitions.flatMap(({ key, label, status }) => {
    const count = counts.get(key) ?? 0;
    return count > 0
      ? [{ key, label, count, color: statusHsl(status) }]
      : [];
  });
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
  // Aggregate/table views present persisted Idle work as Waiting. Use the
  // same purple as the chain-level Waiting state while job details retain the
  // neutral Idle color from STATUS_CONFIG.
  Waiting: statusHsl("Queued"),
  Idle: statusHsl("Queued"),
};

export function displayStatusLabel(status: TickerStatus, idleAsWaiting = false): string {
  return idleAsWaiting && status === "Idle" ? "Waiting" : STATUS_CONFIG[status].label;
}

export function formatStatusLabels(
  statuses: readonly TickerStatus[],
  idleAsWaiting = false
): string {
  return statuses.map((status) => displayStatusLabel(status, idleAsWaiting)).join(", ");
}

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
