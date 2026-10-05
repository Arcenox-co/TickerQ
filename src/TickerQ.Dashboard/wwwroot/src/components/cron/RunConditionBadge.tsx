import { cn } from "@/lib/utils";
import {
  RUN_CONDITION_CONFIG,
  type RunCondition,
} from "@/lib/cron/run-condition";

export function RunConditionBadge({
  condition,
  className,
}: {
  condition: RunCondition;
  className?: string;
}) {
  const cfg = RUN_CONDITION_CONFIG[condition];
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-medium tracking-wide uppercase",
        cfg.bg,
        cfg.text,
        className
      )}
      title={cfg.description}
    >
      {cfg.label}
    </span>
  );
}
