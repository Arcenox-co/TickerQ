import { formatShort, formatTooltip, relativeTime } from "@/lib/cron/format";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";

/**
 * Stacked time cell for tables: the wall-clock timestamp on top — it follows
 * the active timezone, so switching the picker visibly re-renders every
 * table. The relative "X ago" sits underneath (it is timezone-INDEPENDENT: a
 * duration from now is the same in every zone, so it never shifts by design).
 * Hover shows the full precision + relative.
 *
 *   2026-06-08 21:30     ← primary, follows the active timezone
 *   5 minutes ago        ← secondary, zone-independent by nature
 */
export function TimeCell({
  value,
  className,
  muted = false,
}: {
  value: string | number | Date | null | undefined;
  className?: string;
  /** When the value is for a secondary column (e.g. "Created"), dim the primary line. */
  muted?: boolean;
}) {
  const { timezone } = useTimezone();

  if (value == null) {
    return <span className="text-[11px] text-muted-foreground/40">—</span>;
  }

  const relativeInput = typeof value === "string" ? value : new Date(value).toISOString();

  return (
    <div className={cn("flex flex-col leading-tight", className)} title={formatTooltip(value, timezone)}>
      <span
        className={cn(
          "text-[11px] tabular-nums",
          muted ? "text-muted-foreground/70" : "text-muted-foreground"
        )}
      >
        {formatShort(value, timezone)}
      </span>
      <span className="text-[10px] tabular-nums text-muted-foreground/40">
        {relativeTime(relativeInput)}
      </span>
    </div>
  );
}
