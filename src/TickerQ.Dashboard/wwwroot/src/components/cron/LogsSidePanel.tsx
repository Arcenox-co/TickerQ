import { Terminal } from "lucide-react";
import { extractReason, hasCompletion, logColor, mapLogLine, type LogEntry } from "@/lib/cron/log-format";
import { LogTailNotice } from "./LogTailNotice";
import { useTimezone } from "@/lib/timezone";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";
import { StatusBadge } from "@/components/cron/StatusBadges";
import type { TickerStatus } from "@/lib/cron/status-config";
import { formatDuration, relativeTime } from "@/lib/cron/format";
import { useLogTail } from "@/services/hooks";
import { ExceptionDetails } from "./ExceptionDetails";

export type { LogEntry } from "@/lib/cron/log-format";

export interface LogsSidePanelTarget {
  id: string;
  function: string;
  status: TickerStatus;
  scheduledAt?: string | null;
  durationMs?: number | null;
  retries?: { current: number; max: number } | null;
  exceptionMessage?: string | null;
  skippedReason?: string | null;
}

export function LogsSidePanel({
  target,
  onClose,
}: {
  target: LogsSidePanelTarget | null;
  onClose: () => void;
}) {
  const live =
    target?.status === "Idle" ||
    target?.status === "Queued" ||
    target?.status === "InProgress";
  const { data, isLoading, isError, refetch } = useLogTail(target?.id ?? null, live);
  const { timezone } = useTimezone();
  const logs: LogEntry[] = (data?.lines ?? []).map(line => mapLogLine(line, undefined, timezone));

  // Older/evicted tails still show the saved failure reason without duplicating captured outcomes.
  if (target && !hasCompletion(data?.lines ?? [], target.status)) {
    if (target.status === "Failed" && target.exceptionMessage)
      logs.push({ timestamp: "", level: "error", lifecycleEvent: "Completed", status: target.status, message: `[scheduler] Failed: ${extractReason(target.exceptionMessage)}` });
    else if (target.status === "Cancelled" && target.exceptionMessage)
      logs.push({ timestamp: "", level: "warn", lifecycleEvent: "Completed", status: target.status, message: `[scheduler] Cancelled: ${extractReason(target.exceptionMessage)}` });
    else if (target.status === "Skipped" && target.skippedReason)
      logs.push({ timestamp: "", level: "warn", lifecycleEvent: "Completed", status: target.status, message: `[scheduler] Skipped: ${target.skippedReason}` });
  }

  return (
    <Sheet open={target !== null} onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side="right"
        className="data-[side=right]:w-full data-[side=right]:sm:max-w-[640px] bg-surface-0 border-l border-border overflow-y-auto scrollbar-thin p-0 gap-0"
      >
        {target && (
          <>
            <div className="px-6 py-5 border-b border-border space-y-2">
              <SheetTitle className="text-base font-semibold flex items-center gap-2">
                <Terminal className="h-4 w-4 text-primary" />
                {target.function}
              </SheetTitle>
              <SheetDescription className="text-[12px] text-muted-foreground">
                Execution details and log tail.
              </SheetDescription>
              <div className="flex items-center gap-2 pt-1">
                <StatusBadge status={target.status} />
                <span className="font-mono text-[11px] text-muted-foreground/70">
                  {target.id.slice(0, 12)}
                </span>
              </div>
            </div>

            <div className="px-6 py-4 border-b border-border grid grid-cols-3 gap-3 text-[11px]">
              <div>
                <p className="text-muted-foreground/60 uppercase tracking-wider mb-1">
                  Scheduled
                </p>
                <p className="tabular-nums">{relativeTime(target.scheduledAt)}</p>
              </div>
              <div>
                <p className="text-muted-foreground/60 uppercase tracking-wider mb-1">
                  Duration
                </p>
                <p className="tabular-nums">{formatDuration(target.durationMs)}</p>
              </div>
              <div>
                <p className="text-muted-foreground/60 uppercase tracking-wider mb-1">
                  Retries
                </p>
                <p className="tabular-nums">
                  {target.retries
                    ? `${target.retries.current}/${target.retries.max}`
                    : "—"}
                </p>
              </div>
            </div>

            <div className="p-6">
              <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground/60 mb-2">
                Log tail
              </p>
              <div className="rounded-lg border border-border bg-card font-mono text-[11px] divide-y divide-border/30 max-h-[60vh] overflow-y-auto scrollbar-thin">
                <LogTailNotice data={data} isError={isError} onRefresh={() => refetch()} />
                {isLoading ? <p className="p-4">Loading logs…</p> : logs.length === 0 ? (
                  <div className="p-4 text-center text-muted-foreground text-[11px]">
                    {isError ? "Log request failed." : "No captured lines are available. The tail may have expired or been evicted."}
                  </div>
                ) : (
                  logs.map((log, i) => {
                    const color = logColor(log);
                    return (
                      <div
                        key={i}
                        className="flex flex-col sm:flex-row items-start gap-1 sm:gap-2 px-3 py-1.5"
                      >
                        <span className="text-muted-foreground/60 shrink-0 tabular-nums">
                          {log.timestamp}
                        </span>
                        <span className={`min-w-0 flex-1 whitespace-pre-wrap break-words ${color}`}>
                          {log.message}
                        </span>
                      </div>
                    );
                  })
                )}
                {(target.status === "Failed" || target.status === "Cancelled") && (
                  <ExceptionDetails raw={target.exceptionMessage} />
                )}
              </div>
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
