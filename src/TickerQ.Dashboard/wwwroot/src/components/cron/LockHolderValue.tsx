import { LockHolder } from "@/components/cron/LockHolder";

export function LockHolderValue({ value, inheritedFrom, showDetails = false }: { value?: string | null; inheritedFrom?: string; showDetails?: boolean }) {
  return (
    <span className="inline-flex min-w-0 flex-col gap-0.5">
      <LockHolder value={value} showDetails={showDetails} />
      {value && inheritedFrom && <span className="text-[10px] text-muted-foreground/60">From {inheritedFrom}</span>}
    </span>
  );
}
