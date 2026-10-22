export type BaseRunCondition =
  | "OnSuccess"
  | "OnFailure"
  | "OnCancelled"
  | "OnFailureOrCancelled"
  | "OnAnyCompletedStatus"
  | "InProgress"
  | "OnAnyTerminalStatus";

export type GroupRunCondition =
  | "GroupOnSuccess"
  | "GroupOnFailure"
  | "GroupOnCancelled"
  | "GroupOnFailureOrCancelled"
  | "GroupOnAnyCompletedStatus"
  | "GroupOnAnyTerminalStatus";

export type RunCondition = BaseRunCondition | GroupRunCondition;

export interface RunConditionConfig {
  label: string;
  text: string;
  bg: string;
  cssVar: string;
  description: string;
}

export const RUN_CONDITION_CONFIG: Record<BaseRunCondition, RunConditionConfig> = {
  OnSuccess:            { label: "On Success",       text: "text-status-healthy",    bg: "bg-status-healthy/10",    cssVar: "var(--status-healthy)",    description: "Run when parent completes with Done or DueDone" },
  OnFailure:            { label: "On Failure",       text: "text-status-error",      bg: "bg-status-error/10",      cssVar: "var(--status-error)",      description: "Run when parent fails" },
  OnCancelled:          { label: "On Cancelled",     text: "text-status-cancelled",  bg: "bg-status-cancelled/10",  cssVar: "var(--status-cancelled)",  description: "Run when parent is cancelled" },
  OnFailureOrCancelled: { label: "On Fail/Cancel",   text: "text-status-warning",    bg: "bg-status-warning/10",    cssVar: "var(--status-warning)",    description: "Run when parent fails or is cancelled" },
  OnAnyCompletedStatus: { label: "On Any",           text: "text-status-info",       bg: "bg-status-info/10",       cssVar: "var(--status-info)",       description: "Run on any terminal status except Skipped" },
  InProgress:           { label: "Parallel",         text: "text-status-warning",    bg: "bg-status-warning/10",    cssVar: "var(--status-warning)",    description: "Start when the parent enters InProgress" },
  OnAnyTerminalStatus:  { label: "After all settle", text: "text-condition-finally", bg: "bg-condition-finally/10", cssVar: "var(--condition-finally)", description: "Start after Done, DueDone, Failed, Cancelled, or Skipped" },
};

export type ConditionEvaluationState =
  | "met"
  | "not-met"
  | "waiting"
  | "not-evaluated";

export type ConditionJobStatus =
  | "Idle"
  | "Queued"
  | "InProgress"
  | "Done"
  | "DueDone"
  | "Failed"
  | "Cancelled"
  | "Skipped";

/**
 * Describes the start rule independently from the job's final result. A failed
 * job still matched its rule because it was allowed to start.
 */
export function conditionEvaluationState(
  status: ConditionJobStatus,
  hasStarted = false
): ConditionEvaluationState {
  if (status === "Skipped") return "not-met";
  if (status === "Idle" || status === "Queued") return "waiting";
  if (status === "Cancelled" && !hasStarted) return "not-evaluated";
  return "met";
}

export const RUN_CONDITION_OPTIONS: BaseRunCondition[] = [
  "OnSuccess",
  "OnFailure",
  "OnCancelled",
  "OnFailureOrCancelled",
  "OnAnyCompletedStatus",
  "InProgress",
  "OnAnyTerminalStatus",
];

export function isGroupedRunCondition(condition?: string | null): boolean {
  return condition === "InProgress" || condition?.startsWith("Group") === true;
}

export function baseRunCondition(condition: RunCondition): BaseRunCondition {
  switch (condition) {
    case "GroupOnSuccess": return "OnSuccess";
    case "GroupOnFailure": return "OnFailure";
    case "GroupOnCancelled": return "OnCancelled";
    case "GroupOnFailureOrCancelled": return "OnFailureOrCancelled";
    case "GroupOnAnyCompletedStatus": return "OnAnyCompletedStatus";
    case "GroupOnAnyTerminalStatus": return "OnAnyTerminalStatus";
    default: return condition;
  }
}

export function groupedRunCondition(condition: BaseRunCondition): RunCondition {
  switch (condition) {
    case "OnSuccess": return "GroupOnSuccess";
    case "OnFailure": return "GroupOnFailure";
    case "OnCancelled": return "GroupOnCancelled";
    case "OnFailureOrCancelled": return "GroupOnFailureOrCancelled";
    case "OnAnyCompletedStatus": return "GroupOnAnyCompletedStatus";
    case "OnAnyTerminalStatus": return "GroupOnAnyTerminalStatus";
    case "InProgress": return "InProgress";
  }
}

export function runConditionHsl(rc?: RunCondition): string {
  if (!rc) return "hsl(var(--border))";
  return `hsl(${RUN_CONDITION_CONFIG[baseRunCondition(rc)].cssVar})`;
}
