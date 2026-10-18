import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

export function LockHolder({
  value,
  showDetails = false,
  className,
}: {
  value: string | null | undefined;
  showDetails?: boolean;
  className?: string;
}) {
  // Match only TickerQ's process-instance suffix. Legacy owners and node names
  // containing colons must not be shortened by simply splitting on ':'.
  const nodeName = value?.match(/^(.+):[0-9]+:[0-9a-f]{32}$/i)?.[1] ?? value;
  const classes = cn("inline-flex min-w-0 max-w-full flex-col align-middle text-[11px] font-mono text-muted-foreground", className);

  if (!value || nodeName === value)
    return <span className={cn(classes, "break-all")}>{value || "—"}</span>;

  return (
    <span className={classes}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            tabIndex={0}
            className="max-w-full cursor-help truncate rounded-sm underline decoration-muted-foreground/40 decoration-dotted underline-offset-4 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {nodeName}
          </span>
        </TooltipTrigger>
        <TooltipContent align="start" className="block w-80 max-w-[calc(100vw-2rem)] space-y-2 rounded-lg p-3 font-sans">
          <span className="block text-[11px] font-medium text-muted-foreground">Execution owner ID</span>
          <code className="block whitespace-normal break-all rounded-md border border-border/70 bg-surface-1 px-2.5 py-2 font-mono text-[11px] leading-relaxed text-popover-foreground">{value}</code>
        </TooltipContent>
      </Tooltip>
      {showDetails && (
        <span className="mt-1 block space-y-0.5">
          <span className="block font-sans text-[10px] text-muted-foreground/60">Execution owner ID</span>
          <code className="block break-all text-[10px] text-muted-foreground/70">{value}</code>
        </span>
      )}
    </span>
  );
}
