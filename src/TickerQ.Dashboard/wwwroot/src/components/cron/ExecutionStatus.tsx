import type { ChainExecutionSummaryDto, TickerStatus } from "@/services/api-types";
import { cn } from "@/lib/utils";
import { STATUS_CONFIG } from "@/lib/cron/status-config";
import { StatusBadge } from "./StatusBadges";

const states = {
  Waiting: { label: "Waiting", color: STATUS_CONFIG.Queued },
  Running: { label: "Running", color: STATUS_CONFIG.InProgress },
  Completed: { label: "Done", color: STATUS_CONFIG.Done },
  Failed: { label: "Finished with errors", color: STATUS_CONFIG.Failed },
  Cancelled: { label: "Finished with cancellations", color: STATUS_CONFIG.Cancelled },
  Unavailable: { label: "Chain unavailable", color: STATUS_CONFIG.Idle },
} as const;

/** Tables show the whole chain; individual job details still use StatusBadge. */
export function ExecutionStatus({
  status,
  summary,
  isChain = false,
  progressPercent,
  progressMessage,
  displayIdleAsWaiting = false,
}: {
  status: TickerStatus;
  summary?: ChainExecutionSummaryDto | null;
  isChain?: boolean;
  progressPercent?: number | null;
  progressMessage?: string | null;
  /** Uses clearer table wording without changing the persisted Idle status. */
  displayIdleAsWaiting?: boolean;
}) {
  if (!summary && !isChain) {
    if (status === "InProgress" && progressPercent != null) {
      return <RunningProgressBadge percent={progressPercent} message={progressMessage} />;
    }

    return (
      <StatusBadge
        status={status}
        label={displayIdleAsWaiting && status === "Idle" ? "Waiting" : undefined}
      />
    );
  }
  const state = states[summary?.status ?? "Unavailable"];
  const known = summary && summary.status !== "Unavailable";
  return (
    <span
      className={cn(
        "inline-flex w-max items-start gap-1.5 rounded-md px-2 py-1",
        state.color.bg,
        state.color.text
      )}
      title="Overall chain status. Progress counts finished jobs, including the root and all nested jobs. Open the chain to inspect individual jobs."
    >
      <span className={cn("mt-1 h-1.5 w-1.5 shrink-0 rounded-full", state.color.dot)} />
      <span className="flex min-w-0 flex-col items-start">
        <span className="whitespace-nowrap text-[11px] font-medium tracking-wide uppercase">
          {state.label}
        </span>
        {known && (
          <span className="inline-flex items-center whitespace-nowrap text-[9px] font-normal leading-3 tracking-normal opacity-75">
            <span className="tabular-nums" aria-label={`${summary.finishedCount} of ${summary.totalCount} jobs finished`}>
              {summary.finishedCount}/{summary.totalCount}
            </span>
            {summary.failedCount > 0 && <span>· {summary.failedCount} failed</span>}
            {summary.cancelledCount > 0 && <span>· {summary.cancelledCount} cancelled</span>}
            {summary.skippedCount > 0 && <span>· {summary.skippedCount} skipped</span>}
          </span>
        )}
      </span>
    </span>
  );
}

function RunningProgressBadge({
  percent,
  message,
}: {
  percent: number;
  message?: string | null;
}) {
  const value = Math.max(0, Math.min(100, percent));
  const radius = 5;
  const circumference = 2 * Math.PI * radius;
  const dashOffset = circumference * (1 - value / 100);
  const label = `${Math.round(value)}%`;
  const color = STATUS_CONFIG.InProgress;

  return (
    <span
      role="progressbar"
      aria-label={message ? `Job progress: ${message}` : `Job progress: ${label}`}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={value}
      title={message || `Job progress: ${label}`}
      className={cn(
        "inline-flex w-max items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-0.5",
        "text-[11px] font-medium tracking-wide uppercase",
        color.bg,
        color.text
      )}
    >
      <span className="relative h-3.5 w-3.5 shrink-0" aria-hidden="true">
        <svg viewBox="0 0 14 14" className="absolute inset-0 h-3.5 w-3.5 -rotate-90">
          <circle
            cx="7"
            cy="7"
            r={radius}
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            className="opacity-20"
          />
          <circle
            cx="7"
            cy="7"
            r={radius}
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeDasharray={circumference}
            strokeDashoffset={dashOffset}
            className="transition-[stroke-dashoffset] duration-500 ease-out"
          />
        </svg>
        <svg
          viewBox="0 0 10 10"
          className="absolute inset-[3px] h-2 w-2 animate-spin motion-reduce:animate-none"
        >
          <circle
            cx="5"
            cy="5"
            r="3"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeDasharray="5 14"
          />
        </svg>
      </span>
      <span>Running</span>
      <span className="tabular-nums tracking-normal opacity-85">{label}</span>
    </span>
  );
}
