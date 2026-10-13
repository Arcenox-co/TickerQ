export type RunCondition =
  | "OnSuccess"
  | "OnFailure"
  | "OnCancelled"
  | "OnFailureOrCancelled"
  | "OnAnyCompletedStatus"
  | "InProgress";

export interface RunConditionConfig {
  label: string;
  text: string;
  bg: string;
  cssVar: string;
  description: string;
}

export const RUN_CONDITION_CONFIG: Record<RunCondition, RunConditionConfig> = {
  OnSuccess:            { label: "On Success",     text: "text-status-healthy",    bg: "bg-status-healthy/10",    cssVar: "var(--status-healthy)",    description: "Run when parent completes with Done or DueDone" },
  OnFailure:            { label: "On Failure",     text: "text-status-error",      bg: "bg-status-error/10",      cssVar: "var(--status-error)",      description: "Run when parent fails" },
  OnCancelled:          { label: "On Cancelled",   text: "text-status-cancelled",  bg: "bg-status-cancelled/10",  cssVar: "var(--status-cancelled)",  description: "Run when parent is cancelled" },
  OnFailureOrCancelled: { label: "On Fail/Cancel", text: "text-status-warning",    bg: "bg-status-warning/10",    cssVar: "var(--status-warning)",    description: "Run when parent fails or is cancelled" },
  OnAnyCompletedStatus: { label: "On Any",         text: "text-status-info",       bg: "bg-status-info/10",       cssVar: "var(--status-info)",       description: "Run on any terminal status except Skipped" },
  InProgress:           { label: "Parallel",       text: "text-status-skipped",    bg: "bg-status-skipped/10",    cssVar: "var(--status-skipped)",    description: "Run concurrently with parent" },
};

export const RUN_CONDITION_OPTIONS: RunCondition[] = [
  "OnSuccess",
  "OnFailure",
  "OnCancelled",
  "OnFailureOrCancelled",
  "OnAnyCompletedStatus",
  "InProgress",
];

export function runConditionHsl(rc?: RunCondition): string {
  if (!rc) return "hsl(var(--border))";
  return `hsl(${RUN_CONDITION_CONFIG[rc].cssVar})`;
}
