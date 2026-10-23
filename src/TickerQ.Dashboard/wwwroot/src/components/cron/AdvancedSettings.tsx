import type { ReactNode } from "react";
import { Settings2 } from "lucide-react";

export function AdvancedSettings({
  description,
  children,
}: {
  description: string;
  children: ReactNode;
}) {
  return (
    <section className="overflow-hidden rounded-xl border border-border/70 bg-surface-0/55 shadow-sm">
      <div className="flex items-center gap-3 px-4 py-3.5">
        <span className="inline-flex size-8 shrink-0 items-center justify-center rounded-lg bg-surface-2 text-muted-foreground">
          <Settings2 className="size-4" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-[12px] font-semibold text-foreground">
            Advanced settings
          </span>
          <span className="mt-0.5 block text-[10px] leading-relaxed text-muted-foreground">
            {description}
          </span>
        </span>
      </div>
      <div className="space-y-5 px-4 pb-4 pt-1">{children}</div>
    </section>
  );
}

export function AdvancedSettingsGroup({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div>
        <h4 className="text-[10px] font-semibold uppercase tracking-wider text-foreground/75">
          {title}
        </h4>
        {description && (
          <p className="mt-0.5 text-[10px] leading-relaxed text-muted-foreground">
            {description}
          </p>
        )}
      </div>
      {children}
    </section>
  );
}
