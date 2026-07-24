import { useEffect, useMemo, useState } from "react";
import { CronExpressionParser } from "cron-parser";
import cronstrue from "cronstrue";
import { AlertTriangle, CalendarClock } from "lucide-react";
import { cn } from "@/lib/utils";
import { formatAbsolute } from "@/lib/cron/format";
import { useTimezone } from "@/lib/timezone";

/**
 * TickerQ's scheduler runs NCrontab with IncludingSeconds=true, so it only
 * accepts 6-field cron. Promote 5-field input to 6-field (prepending "0 ").
 */
export function toSchedulerCronExpression(expression: string): string {
  const trimmed = expression?.trim() ?? "";
  if (!trimmed) return trimmed;
  const fieldCount = trimmed.split(/\s+/).length;
  return fieldCount === 5 ? `0 ${trimmed}` : trimmed;
}

export function CronExpressionPreview({
  expression,
  className,
}: {
  expression: string;
  className?: string;
}) {
  const [tickKey, setTickKey] = useState(0);
  const { timezone } = useTimezone();
  useEffect(() => {
    const id = window.setInterval(() => setTickKey((k) => k + 1), 15_000);
    return () => window.clearInterval(id);
  }, []);

  const result = useMemo(() => {
    const trimmed = expression?.trim();
    if (!trimmed) return { kind: "empty" as const };
    const normalized = toSchedulerCronExpression(trimmed);
    try {
      const description = cronstrue.toString(normalized, {
        use24HourTimeFormat: true,
      });
      const iter = CronExpressionParser.parse(normalized, {
        currentDate: new Date(),
      });
      return {
        kind: "ok" as const,
        description,
        next: iter.next().toDate(),
      };
    } catch (err) {
      return {
        kind: "error" as const,
        message: err instanceof Error ? err.message : "Invalid expression",
      };
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [expression, tickKey]);

  if (result.kind === "empty") return null;

  if (result.kind === "error") {
    return (
      <p
        className={cn(
          "flex items-center gap-1 text-[11px] text-status-error",
          className
        )}
      >
        <AlertTriangle className="h-3 w-3 shrink-0" />
        {result.message}
      </p>
    );
  }

  return (
    <p
      className={cn(
        "flex flex-wrap items-center gap-1 text-[11px] text-muted-foreground",
        className
      )}
    >
      <CalendarClock className="h-3 w-3 text-primary shrink-0" />
      <span className="text-foreground/80">{result.description}</span>
      <span className="text-muted-foreground/40">·</span>
      <span className="text-muted-foreground/80">
        Next:{" "}
        <span className="font-mono tabular-nums text-foreground/80">
          {formatAbsolute(result.next, timezone)}
        </span>
      </span>
    </p>
  );
}

export function nextRunFromExpression(expression: string): Date | null {
  const trimmed = expression?.trim();
  if (!trimmed) return null;
  try {
    return CronExpressionParser.parse(toSchedulerCronExpression(trimmed), {
      currentDate: new Date(),
    })
      .next()
      .toDate();
  } catch {
    return null;
  }
}

export function isValidCronExpression(expression: string): boolean {
  const trimmed = expression?.trim();
  if (!trimmed) return false;
  try {
    CronExpressionParser.parse(toSchedulerCronExpression(trimmed));
    return true;
  } catch {
    return false;
  }
}
