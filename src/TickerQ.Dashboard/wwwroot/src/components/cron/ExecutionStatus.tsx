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
export function ExecutionStatus({ status, summary, isChain = false }: {
  status: TickerStatus;
  summary?: ChainExecutionSummaryDto | null;
  isChain?: boolean;
}) {
  if (!summary && !isChain) return <StatusBadge status={status} />;
  const state = states[summary?.status ?? "Unavailable"];
  const known = summary && summary.status !== "Unavailable";
  return (
    <span
      className={cn("inline-flex w-max items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-0.5 text-[11px] font-medium tracking-wide uppercase", state.color.bg, state.color.text)}
      title="Overall chain status. Progress counts finished jobs, including the root and all nested jobs. Open the chain to inspect individual jobs."
    >
      <span className={cn("h-1.5 w-1.5 shrink-0 rounded-full", state.color.dot)} />
      <span>{state.label}</span>
      {known && <>
        <span className="tabular-nums tracking-normal opacity-80" aria-label={`${summary.finishedCount} of ${summary.totalCount} jobs finished`}>
          · {summary.finishedCount}/{summary.totalCount}
        </span>
        {summary.failedCount > 0 && <span className="normal-case tracking-normal text-status-error">· {summary.failedCount} failed</span>}
        {summary.cancelledCount > 0 && <span className="normal-case tracking-normal">· {summary.cancelledCount} cancelled</span>}
        {summary.skippedCount > 0 && <span className="normal-case tracking-normal opacity-80">· {summary.skippedCount} skipped</span>}
      </>}
    </span>
  );
}
