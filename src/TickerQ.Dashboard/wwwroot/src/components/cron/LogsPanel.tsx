import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, Loader2, Terminal } from "lucide-react";
import { useLogTail } from "@/services/hooks";
import { extractReason, hasCompletion, logColor, mapLogLine, type LogEntry } from "@/lib/cron/log-format";
import { useTimezone } from "@/lib/timezone";
import type { TickerStatus } from "@/lib/cron/status-config";
import { cn } from "@/lib/utils";
import { LogTailNotice } from "./LogTailNotice";

/**
 * Embeddable log tail with bounded polling and a final refresh after completion.
 */
export function LogsPanel({
  functionName,
  status,
  tickerId,
  exceptionMessage,
  skippedReason,
  className,
}: {
  functionName: string;
  status: TickerStatus;
  tickerId: string | null;
  exceptionMessage?: string | null;
  skippedReason?: string | null;
  executedAt?: string | null;
  className?: string;
}) {
  // Poll the tail while the ticker is pending/running so lines stream in live;
  // after completion, the hook fetches the final persisted tail.
  const live = status === "Idle" || status === "Queued" || status === "InProgress";
  const [collapsed, setCollapsed] = useState(false);
  const { data, isLoading, isError, refetch } = useLogTail(tickerId, live, !collapsed);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const { timezone } = useTimezone();

  const logs = useMemo<LogEntry[]>(() => {
    const lines = (data?.lines ?? []).map((l) => mapLogLine(l, undefined, timezone));
    if (hasCompletion(data?.lines ?? [], status)) return lines;
    if (status === "Failed" && exceptionMessage)
      return [...lines, { timestamp: "", level: "error", lifecycleEvent: "Completed", status, message: `[scheduler] ${extractReason(exceptionMessage)}` }];
    if (status === "Cancelled" && exceptionMessage)
      return [...lines, { timestamp: "", level: "warn", lifecycleEvent: "Completed", status, message: `[scheduler] Cancelled: ${extractReason(exceptionMessage)}` }];
    if (status === "Skipped" && skippedReason)
      return [...lines, { timestamp: "", level: "warn", lifecycleEvent: "Completed", status, message: `[scheduler] Skipped: ${skippedReason}` }];
    return lines;
  }, [data, status, exceptionMessage, skippedReason, timezone]);

  useEffect(() => {
    if (status === "InProgress" && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [logs, status]);

  return (
    <div className={cn("@container rounded-lg border border-border bg-card overflow-hidden", className)}>
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="w-full flex items-center justify-between px-3 py-1.5 border-b border-border/50 text-[11px]"
      >
        <span className="flex items-center gap-1.5 text-muted-foreground">
          <Terminal className="h-3 w-3 text-primary" />
          Logs
          <span className="font-mono text-muted-foreground/60">{functionName}</span>
          {status === "InProgress" && (
            <Loader2 className="h-3 w-3 animate-spin text-status-warning" />
          )}
        </span>
        <ChevronDown className={cn("h-3 w-3 transition-transform", collapsed && "-rotate-90")} />
      </button>
      {!collapsed && <LogTailNotice data={data} isError={isError} onRefresh={() => { void refetch(); }} />}
      {!collapsed && (
        <div
          ref={scrollRef}
          className="font-mono text-[11px] leading-[20px] p-2 max-h-[320px] overflow-y-auto scrollbar-thin"
        >
          {isLoading ? (
            <div className="flex items-center gap-2 px-1.5 py-1 text-muted-foreground">
              <Loader2 className="h-3 w-3 animate-spin" /> Loading logs…
            </div>
          ) : logs.length === 0 ? (
            <div className="px-1.5 py-1 text-muted-foreground/60">
              {status === "InProgress"
                ? "Waiting for the first log line…"
                : "No captured lines are available. The tail may have expired or been evicted."}
            </div>
          ) : (
            logs.map((log, i) => {
              const color = logColor(log);
              return (
                <div
                  key={i}
                  className="flex flex-col @md:flex-row items-start gap-0.5 @md:gap-2 px-1.5 py-[1px] rounded-sm hover:bg-surface-2/30"
                >
                  {log.timestamp && (
                    <span className="text-muted-foreground/50 shrink-0 tabular-nums">
                      {log.timestamp}
                    </span>
                  )}
                  <span className={cn("min-w-0 flex-1 whitespace-pre-wrap break-words [overflow-wrap:anywhere]", color)}>
                    {log.message}
                  </span>
                </div>
              );
            })
          )}
        </div>
      )}
    </div>
  );
}
