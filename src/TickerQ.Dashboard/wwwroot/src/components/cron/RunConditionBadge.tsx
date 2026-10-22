import { cn } from "@/lib/utils";
import {
  RUN_CONDITION_CONFIG,
  baseRunCondition,
  type ConditionEvaluationState,
  type RunCondition,
} from "@/lib/cron/run-condition";

const CONDITION_EVALUATION_CONFIG: Record<
  ConditionEvaluationState,
  { label: string; className: string; description: string }
> = {
  met: {
    label: "Start condition met",
    className: "border-status-healthy/25 bg-status-healthy/10 text-status-healthy",
    description: "The start rule matched and released this job.",
  },
  "not-met": {
    label: "Start condition not met",
    className: "border-dotted border-muted-foreground/35 bg-transparent text-muted-foreground/65",
    description: "The start rule did not match, so this job was skipped.",
  },
  waiting: {
    label: "Waiting for condition",
    className: "border-dotted border-status-info/35 bg-status-info/5 text-status-info/70",
    description: "The start rule has not been evaluated yet.",
  },
  "not-evaluated": {
    label: "Condition not evaluated",
    className: "border-dotted border-muted-foreground/30 bg-transparent text-muted-foreground/55",
    description: "The job was cancelled before its start rule released it.",
  },
};

export function ConditionEvaluationBadge({
  state,
  className,
}: {
  state: ConditionEvaluationState;
  className?: string;
}) {
  const config = CONDITION_EVALUATION_CONFIG[state];

  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center rounded-md border px-1.5 py-0.5 text-[9px] font-medium tracking-wide",
        config.className,
        className
      )}
      title={config.description}
    >
      {config.label}
    </span>
  );
}

export function RunConditionBadge({
  condition,
  executionState = "complete",
  label,
  description,
  className,
}: {
  condition: RunCondition;
  /** Controls whether the rule is shown as completed, still pending, or unused. */
  executionState?: "complete" | "pending" | "not-run";
  /** Optional user-facing wording when the stored rule has contextual meaning. */
  label?: string;
  description?: string;
  className?: string;
}) {
  const visibleCondition = baseRunCondition(condition);
  const cfg = RUN_CONDITION_CONFIG[visibleCondition];
  const displayLabel = label ?? cfg.label;
  const displayDescription = description ?? cfg.description;
  const subdued = executionState !== "complete";
  const executionHint = executionState === "not-run"
    ? "This rule did not release the job, so it did not run."
    : "This job has not completed through this rule yet.";

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md border border-transparent px-2 py-0.5 text-[10px] font-medium tracking-wide uppercase",
        cfg.bg,
        cfg.text,
        subdued && "border-dotted border-current/35 bg-transparent opacity-45",
        className
      )}
      title={subdued
        ? `${displayDescription}. ${executionHint} Rule: ${visibleCondition}.`
        : `${displayDescription}. Rule: ${visibleCondition}.`}
    >
      <span>{displayLabel}</span>
      <span aria-hidden="true" className="opacity-40">·</span>
      <code className="font-mono text-[9px] font-normal normal-case tracking-normal opacity-75">
        {visibleCondition}
      </code>
    </span>
  );
}
