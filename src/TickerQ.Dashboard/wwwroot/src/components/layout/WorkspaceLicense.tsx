import { cn } from "@/lib/utils";
import { useLicense } from "@/services/hooks";

export function WorkspaceLicense({ compact = false, className }: { compact?: boolean; className?: string }) {
  const { data: license } = useLicense();
  const licenseType = license?.isEvaluation ? "Evaluation" : license?.plan?.trim();
  const workspaceName = license?.workspaceName?.trim();
  if (!licenseType) return null;

  const label = [workspaceName, licenseType].filter(Boolean).join(" · ");

  return (
    <div
      title={label}
      className={cn(
        "flex min-w-0 items-center gap-1.5 text-[11px]",
        compact
          ? "shrink-0 rounded-lg border border-border bg-surface-1 px-2 py-1"
          : "border-b border-border/60 bg-black/[0.025] px-4 py-2.5 shadow-[inset_0_2px_5px_rgb(0_0_0/0.06)] dark:bg-black/15 dark:shadow-[inset_0_2px_6px_rgb(0_0_0/0.2)]",
        className
      )}
    >
      {workspaceName && (
        <>
          <span className={cn("truncate font-medium text-card-foreground", compact ? "max-w-20 sm:max-w-40 lg:max-w-60" : "text-xs")}>
            {workspaceName}
          </span>
          <span className="shrink-0 text-muted-foreground/40" aria-hidden="true">·</span>
        </>
      )}
      <span className="shrink-0 font-medium text-primary">{licenseType}</span>
    </div>
  );
}
