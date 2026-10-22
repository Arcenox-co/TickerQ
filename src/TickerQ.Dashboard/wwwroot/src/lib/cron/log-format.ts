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

export interface ExceptionDetails {
  type: string | null;
  message: string;
  method: string | null;
  sourceFile: string | null;
  sourceLine: number | null;
  stackTrace: string | null;
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
export function parseExceptionDetails(raw: string): ExceptionDetails {
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    if (parsed && typeof parsed === "object") {
      const readString = (pascal: string, camel: string) => {
        const value = parsed[pascal] ?? parsed[camel];
        return typeof value === "string" && value.trim() ? value : null;
      };
      const rawLine = parsed.SourceLine ?? parsed.sourceLine;
      const sourceLine = typeof rawLine === "number" && Number.isFinite(rawLine) && rawLine > 0
        ? Math.trunc(rawLine)
        : null;
      const message = readString("Message", "message");
      if (message) {
        return {
          type: readString("Type", "type") ?? readString("ExceptionType", "exceptionType"),
          message,
          method: readString("Method", "method"),
          sourceFile: readString("SourceFile", "sourceFile"),
          sourceLine,
          stackTrace: applicationStackTrace(readString("StackTrace", "stackTrace")),
        };
      }
    }
  } catch {
    // Legacy/provider-generated reasons are deliberately plain text.
  }
  return { type: null, message: raw, method: null, sourceFile: null, sourceLine: null, stackTrace: null };
}

/**
 * Older persisted failures can contain the generated delegate and scheduler stack below the
 * application frames. Trim it in the client as well so existing rows get the same focused view
 * as failures captured by current runtimes.
 */
function applicationStackTrace(stackTrace: string | null): string | null {
  if (!stackTrace) return null;

  const lines = stackTrace.split(/\r?\n/);
  const boundary = lines.findIndex(line =>
    line.includes("TickerQInstanceFactoryExtensions.") ||
    line.includes("TickerQ.TickerExecutionTaskHandler.") ||
    line.includes("TickerQ.TickerQThreadPool."));
  const visible = (boundary >= 0 ? lines.slice(0, boundary) : lines)
    .filter(line => !line.trimStart().startsWith("--- End of stack trace"))
    .join("\n")
    .trim();

  return visible || null;
}

export function extractReason(raw: string): string {
  return parseExceptionDetails(raw).message;
}
