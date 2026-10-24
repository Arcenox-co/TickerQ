import { useId } from "react";
import { Checkbox } from "@/components/ui/checkbox";
import { FieldInfo } from "@/components/form-fields/field-info";

export function CronOverlapField({ checked, onChange, isChain = false, compact = false }: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  isChain?: boolean;
  compact?: boolean;
}) {
  const id = useId();
  const explanation = isChain
    ? "Skips new runs while an earlier chain is unfinished."
    : "Skips a new occurrence while this cron job is active. Other jobs using the same function remain independent.";

  if (compact) {
    return (
      <div className="space-y-1.5">
        <div className="flex items-center justify-between gap-2">
          <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
            Overlap
          </span>
          <FieldInfo>{explanation} When disabled, occurrences may overlap.</FieldInfo>
        </div>
        <label
          htmlFor={id}
          className="flex h-9 cursor-pointer items-center gap-2 rounded-md border border-border bg-surface-0 px-3 text-xs text-foreground transition-colors hover:bg-surface-1"
        >
          <Checkbox
            id={id}
            checked={checked}
            onCheckedChange={value => onChange(value === true)}
            className="size-4 shrink-0 border-2 border-muted-foreground/70"
          />
          <span className="truncate">Skip if already running</span>
        </label>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-x-2 gap-y-1">
      <Checkbox id={id} checked={checked} onCheckedChange={value => onChange(value === true)}
        aria-describedby={`${id}-hint`} className="mt-0.5 size-4 shrink-0 border-2 border-muted-foreground/70 bg-surface-2" />
      <label htmlFor={id} className="text-xs font-semibold leading-5 text-foreground cursor-pointer">
        Skip if already running
      </label>
      <FieldInfo>
        {explanation} When disabled, occurrences may overlap.
      </FieldInfo>
      <p id={`${id}-hint`} className="col-start-2 col-span-2 text-[10px] text-muted-foreground leading-4">
        Prevent overlapping occurrences
      </p>
    </div>
  );
}
