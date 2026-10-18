import { useId } from "react";
import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils";

export function CronOverlapField({ checked, onChange, isChain = false, compact = false }: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  isChain?: boolean;
  compact?: boolean;
}) {
  const id = useId();
  return (
    <div className={cn("grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-1",
      compact ? "rounded-md border border-border bg-surface-0/60 px-3 py-2" : "rounded-lg border border-border/50 bg-surface-0/30 p-3")}>
      <Checkbox id={id} checked={checked} onCheckedChange={value => onChange(value === true)}
        aria-describedby={`${id}-hint`} className="mt-0.5 size-4 shrink-0 border-2 border-muted-foreground/70 bg-surface-2" />
      <label htmlFor={id} className="text-xs font-semibold leading-5 text-foreground cursor-pointer">
        Skip if already running
      </label>
      <p id={`${id}-hint`} className="col-start-2 text-[11px] text-muted-foreground leading-4">
        {isChain
          ? "Skip new runs while an earlier chain is unfinished."
          : "Skip a new run while this cron job is active on this scheduler host. Other cron jobs using the same function are independent."}
        {" "}When off, runs may overlap. Skipped runs appear in history.
      </p>
    </div>
  );
}
