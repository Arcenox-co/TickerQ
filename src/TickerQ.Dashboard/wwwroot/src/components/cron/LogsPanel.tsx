import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, Loader2, Terminal } from "lucide-react";
import { useLogTail } from "@/services/hooks";
import { formatLogTimestamp } from "@/lib/cron/format";
import { useTimezone } from "@/lib/timezone";
import type { TickerStatus } from "@/lib/cron/status-config";
import type { TickerLogLine } from "@/services/api-types";
import { cn } from "@/lib/utils";

export type LogLevel = "info" | "warn" | "error" | "debug" | "success";

export const LEVEL_STYLES: Record<LogLevel, { color: string }> = {
  info: { color: "text-muted-foreground" },
  debug: { color: "text-muted-foreground/50" },
  warn: { color: "text-status-warning" },
  error: { color: "text-status-error" },
  success: { color: "text-status-healthy" },
};

/**
 * Line color for a log level, given the execution's final status. On a
 * successful run the neutral (info/debug) lines render green — warn/error
 * keep their own colors so mixed runs stay readable.
 */
export function levelColor(level: LogLevel, succeeded: boolean): string {
  if (succeeded && (level === "info" || level === "debug"))
    return LEVEL_STYLES.success.color;
  return LEVEL_STYLES[level].color;
}

export interface LogEntry {
  timestamp: string;
  level: LogLevel;
  message: string;
}

export function mapLevel(level: number): LogLevel {
  return level >= 4 ? "error" : level === 3 ? "warn" : level <= 1 ? "debug" : "info";
}

export function mapLogLine(line: TickerLogLine, prefix?: string, tz?: string): LogEntry {
  const src = prefix ?? (line.source || "");
  return {
    timestamp: formatLogTimestamp(line.unixMs, tz),
    level: mapLevel(line.level),
    message: src ? `[${src}] ${line.message}` : line.message,
  };
}

/**
 * ExceptionMessage is either a plain reason string (e.g. the stale-job watchdog)
 * or the executor's serialized {"Message","StackTrace"} JSON — show just the
 * human-readable message either way.
 */
export function extractReason(raw: string): string {
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed.Message === "string") return parsed.Message;
  } catch {
    // plain string — use as-is
  }
  return raw;
}

/**
 * Embeddable terminal-style log tail. Tail-only (no SignalR live append — the
 * dashboard has no realtime client); refreshes when React Query invalidates.
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
  // once it lands on a terminal status the tail is frozen and polling stops.
  const live = status === "Idle" || status === "Queued" || status === "InProgress";
  const { data, isLoading } = useLogTail(tickerId, live);
  const [collapsed, setCollapsed] = useState(false);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const { timezone } = useTimezone();
  const succeeded = status === "Done" || status === "DueDone";

  const logs = useMemo<LogEntry[]>(() => {
    const lines = (data?.lines ?? []).map((l) => mapLogLine(l, undefined, timezone));
    if (lines.length === 0) {
      if (status === "Failed" && exceptionMessage)
        return [{ timestamp: "", level: "error", message: `[scheduler] ${extractReason(exceptionMessage)}` }];
      if (status === "Cancelled" && exceptionMessage)
        return [{ timestamp: "", level: "warn", message: `[scheduler] Cancelled: ${extractReason(exceptionMessage)}` }];
      if (status === "Skipped" && skippedReason)
        return [{ timestamp: "", level: "warn", message: `[scheduler] Skipped: ${skippedReason}` }];
    }
    return lines;
  }, [data, status, exceptionMessage, skippedReason, timezone]);

  useEffect(() => {
    if (status === "InProgress" && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [logs, status]);

  return (
    <div className={cn("rounded-lg border border-border bg-card overflow-hidden", className)}>
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
                : "No logs available. Logs appear while a function executes and are kept in memory for ~30 minutes."}
            </div>
          ) : (
            logs.map((log, i) => {
              const color = levelColor(log.level, succeeded);
              return (
                <div
                  key={i}
                  className="flex items-start gap-2 px-1.5 py-[1px] rounded-sm hover:bg-surface-2/30"
                >
                  {log.timestamp && (
                    <span className="text-muted-foreground/50 shrink-0 tabular-nums">
                      {log.timestamp}
                    </span>
                  )}
                  <span className={cn("flex-1 whitespace-pre-wrap break-all", color)}>
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
