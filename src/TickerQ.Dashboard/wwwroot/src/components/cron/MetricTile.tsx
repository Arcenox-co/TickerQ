import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

export function MetricTile({
  icon: Icon,
  label,
  value,
  iconClassName = "text-primary",
}: {
  icon: LucideIcon;
  label: string;
  value: ReactNode;
  iconClassName?: string;
}) {
  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="flex items-center gap-2 mb-2">
        <Icon className={`h-3.5 w-3.5 ${iconClassName}`} />
        <p className="text-xs text-muted-foreground">{label}</p>
      </div>
      <p className="text-2xl font-bold font-mono text-foreground tabular-nums">
        {value}
      </p>
    </div>
  );
}
