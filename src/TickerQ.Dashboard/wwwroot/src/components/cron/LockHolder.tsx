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
            className="max-w-full truncate rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {nodeName}
          </span>
        </TooltipTrigger>
        <TooltipContent className="block max-w-xs space-y-1">
          <span className="block text-[10px] font-medium">Execution owner ID</span>
          <code className="block break-all text-[11px]">{value}</code>
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
