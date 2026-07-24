import { Terminal } from "lucide-react";
import { extractReason } from "@/components/cron/LogsPanel";
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
import type { TickerLogLine } from "@/services/api-types";

export interface LogEntry {
  timestamp: string;
  level: "info" | "warn" | "error" | "debug" | "success";
  message: string;
}

const LEVEL_STYLES: Record<LogEntry["level"], { color: string }> = {
  info:    { color: "text-muted-foreground" },
  debug:   { color: "text-muted-foreground/50" },
  warn:    { color: "text-status-warning" },
  error:   { color: "text-status-error" },
  success: { color: "text-status-healthy" },
};

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

function mapLogLine(line: TickerLogLine): LogEntry {
  const level: LogEntry["level"] =
    line.level >= 4 ? "error" : line.level === 3 ? "warn" : line.level <= 1 ? "debug" : "info";
  return {
    timestamp: new Date(line.unixMs).toISOString().replace("T", " ").slice(0, 23),
    level,
    message: line.source ? `[${line.source}] ${line.message}` : line.message,
  };
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
  const { data } = useLogTail(target?.id ?? null, live);
  let logs: LogEntry[] = (data?.lines ?? []).map(mapLogLine);

  // No captured lines — fall back to the scheduler-side outcome reason so
  // Failed/Cancelled/Skipped executions still explain themselves.
  if (logs.length === 0 && target) {
    if (target.status === "Failed" && target.exceptionMessage)
      logs = [{ timestamp: "", level: "error", message: `[scheduler] ${extractReason(target.exceptionMessage)}` }];
    else if (target.status === "Cancelled" && target.exceptionMessage)
      logs = [{ timestamp: "", level: "warn", message: `[scheduler] Cancelled: ${extractReason(target.exceptionMessage)}` }];
    else if (target.status === "Skipped" && target.skippedReason)
      logs = [{ timestamp: "", level: "warn", message: `[scheduler] Skipped: ${target.skippedReason}` }];
  }

  return (
    <Sheet open={target !== null} onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side="right"
        className="w-[640px] sm:max-w-[640px] bg-surface-0 border-l border-border overflow-y-auto scrollbar-thin p-0 gap-0"
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
                {logs.length === 0 ? (
                  <div className="p-4 text-center text-muted-foreground text-[11px]">
                    No logs captured for this execution.
                  </div>
                ) : (
                  logs.map((log, i) => {
                    const succeeded =
                      target?.status === "Done" || target?.status === "DueDone";
                    const color =
                      succeeded && (log.level === "info" || log.level === "debug")
                        ? LEVEL_STYLES.success.color
                        : LEVEL_STYLES[log.level].color;
                    return (
                      <div
                        key={i}
                        className="flex items-start gap-2 px-3 py-1.5"
                      >
                        <span className="text-muted-foreground/60 shrink-0 tabular-nums">
                          {log.timestamp}
                        </span>
                        <span className={`flex-1 whitespace-pre-wrap ${color}`}>
                          {log.message}
                        </span>
                      </div>
                    );
                  })
                )}
              </div>
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
