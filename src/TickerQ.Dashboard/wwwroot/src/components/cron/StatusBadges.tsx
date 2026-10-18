import { GitBranch } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  STATUS_CONFIG,
  healthDotClass,
  type HealthColor,
  type TickerStatus,
} from "@/lib/cron/status-config";

export function StatusBadge({
  status,
  className,
}: {
  status: TickerStatus;
  className?: string;
}) {
  const c = STATUS_CONFIG[status];
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-md px-2 py-0.5 text-[11px] font-medium tracking-wide uppercase",
        c.bg,
        c.text,
        className
      )}
    >
      <span className={cn("h-1.5 w-1.5 rounded-full", c.dot)} />
      {c.label}
    </span>
  );
}

export function HealthDot({
  status,
  className,
}: {
  status: HealthColor;
  className?: string;
}) {
  const dotCls = healthDotClass(status);
  return (
    <span className={cn("relative inline-flex h-2 w-2", className)}>
      <span
        className={cn(
          "absolute inline-flex h-full w-full rounded-full opacity-40 animate-pulse-dot",
          dotCls
        )}
      />
      <span className={cn("relative inline-flex h-2 w-2 rounded-full", dotCls)} />
    </span>
  );
}

export function TypeBadge({
  type,
  chainChildCount,
  isChain = (chainChildCount ?? 0) > 0,
  onChainClick,
}: {
  type: "Time" | "Cron";
  chainChildCount?: number;
  /** Cron executions link to their materialized chain without a child count. */
  isChain?: boolean;
  /** When provided + this is a chain root, the badge becomes a button that opens the chain flowchart. */
  onChainClick?: () => void;
}) {
  const showChain = isChain;
  const interactive = showChain && !!onChainClick;
  const className = cn(
    "inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-[11px] font-medium tracking-wide uppercase",
    type === "Cron" ? "bg-primary/10 text-primary" : "bg-status-info/10 text-status-info",
    interactive &&
      "cursor-pointer transition-colors hover:brightness-110 focus:outline-none focus:ring-1 focus:ring-current/40"
  );
  const content = (
    <>
      {type}
      {showChain && (
        <>
          <GitBranch className="h-2.5 w-2.5 shrink-0 opacity-80" />
          {chainChildCount != null && chainChildCount > 0 && (
            <span className="text-[9px] tabular-nums">{chainChildCount}</span>
          )}
        </>
      )}
    </>
  );
  if (interactive) {
    return (
      <button
        type="button"
        title="View chain flowchart"
        onClick={(e) => {
          e.stopPropagation();
          onChainClick();
        }}
        className={className}
      >
        {content}
      </button>
    );
  }
  return <span className={className}>{content}</span>;
}

/** The job definition's shape, shared by the cron and time ticker lists. */
export function ChainTypeBadge({ isChain, childCount, onClick }: {
  isChain: boolean;
  childCount?: number;
  onClick?: () => void;
}) {
  if (!isChain) return <span className="text-[11px] text-muted-foreground">Standalone</span>;

  const className = "inline-flex items-center gap-1 rounded-md bg-primary/10 px-2 py-0.5 text-[11px] font-medium text-primary uppercase tracking-wide";
  const content = <>
    <GitBranch aria-hidden="true" className="h-2.5 w-2.5" />
    Chain
    {childCount != null && childCount > 0 && <span className="text-[9px] tabular-nums">{childCount}</span>}
  </>;
  return onClick ? (
    <button type="button" title="View chain" className={cn(className, "transition-colors hover:bg-primary/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring")}
      onClick={event => { event.stopPropagation(); onClick(); }}>
      {content}
    </button>
  ) : <span className={className}>{content}</span>;
}

export function PriorityBadge({ priority }: { priority: string }) {
  return (
    <span className="text-[11px] font-medium text-muted-foreground">
      {priority}
    </span>
  );
}
