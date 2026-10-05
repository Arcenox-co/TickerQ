import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export function PageHeader({
  title,
  count,
  description,
  children,
  className,
}: {
  title: string;
  count?: number;
  description?: string;
  children?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("flex items-start justify-between gap-4", className)}>
      <div className="space-y-1">
        <div className="flex items-center gap-2.5">
          <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
          {count !== undefined && (
            <span className="text-xs font-medium tabular-nums text-muted-foreground bg-surface-2 rounded-md px-1.5 py-0.5">
              {count}
            </span>
          )}
        </div>
        {description && (
          <p className="text-sm text-muted-foreground">{description}</p>
        )}
      </div>
      {children && (
        <div className="flex items-center gap-2 shrink-0">{children}</div>
      )}
    </div>
  );
}

export function DataCard({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("rounded-lg border border-border bg-card", className)}>
      {children}
    </div>
  );
}

export function DataCardHeader({
  title,
  children,
  className,
}: {
  title: string;
  children?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex items-center justify-between px-4 py-3 border-b border-border",
        className
      )}
    >
      <h3 className="text-sm font-medium">{title}</h3>
      {children}
    </div>
  );
}
