import { STATUS_CONFIG, type TickerStatus } from "./status-config.ts";
import { formatLogTimestamp } from "./format.ts";
import type { TickerLogLine } from "@/services/api-types";

export type LogLevel = "info" | "warn" | "error" | "debug";

export const LEVEL_STYLES: Record<LogLevel, { color: string }> = {
  info: { color: "text-foreground" },
  debug: { color: "text-muted-foreground" },
  warn: { color: "text-status-warning" },
  error: { color: "text-status-error" },
};

/** Log severity determines text color independently of the job's outcome. */
export function levelColor(level: LogLevel): string {
  return LEVEL_STYLES[level].color;
}

export interface LogEntry {
  timestamp: string;
  level: LogLevel;
  message: string;
  lifecycleEvent?: TickerLogLine["lifecycleEvent"];
  status?: TickerStatus | null;
}

/** Only structured scheduler events use job-status colors. Application logs keep their severity. */
export function logColor(entry: LogEntry): string {
  return entry.lifecycleEvent && entry.status && STATUS_CONFIG[entry.status]
    ? STATUS_CONFIG[entry.status].text : levelColor(entry.level);
}

export function hasCompletion(lines: TickerLogLine[], status: TickerStatus): boolean {
  const last = lines.filter(line => line.lifecycleEvent).at(-1);
  return (last?.lifecycleEvent === "Completed" || last?.lifecycleEvent === "ChainLaunched") && last.status === status;
}

export function mapLevel(level: number): LogLevel {
  return level >= 4 ? "error" : level === 3 ? "warn" : level <= 1 ? "debug" : "info";
}

export function mapLogLine(line: TickerLogLine, prefix?: string, tz?: string): LogEntry {
  const src = [prefix, line.source].filter(Boolean).join(" · ");
  return {
    timestamp: formatLogTimestamp(line.unixMs, tz),
    level: mapLevel(line.level),
    lifecycleEvent: line.lifecycleEvent,
    status: line.status,
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
