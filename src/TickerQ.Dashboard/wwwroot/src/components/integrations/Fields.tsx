import type { ReactNode } from "react";
import { Webhook } from "lucide-react";
import slackLogo from "@/assets/integrations/slack.svg";
import teamsLogo from "@/assets/integrations/microsoft-teams.svg";
import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils";

export function ProviderIcon({
  provider,
  small = false,
}: {
  provider: string;
  small?: boolean;
}) {
  const logo =
    provider === "slack"
      ? slackLogo
      : provider === "teams"
        ? teamsLogo
        : null;
  return (
    <span
      aria-hidden="true"
      className={cn(
        "inline-flex shrink-0 items-center justify-center rounded-xl border",
        small ? "size-9" : "size-11",
        provider === "slack"
          ? "border-violet-500/20 bg-violet-500/10 text-violet-400"
          : provider === "teams"
            ? "border-indigo-500/20 bg-indigo-500/10 text-indigo-400"
            : "border-primary/20 bg-primary/10 text-primary",
      )}
    >
      {logo ? (
        <img
          src={logo}
          alt=""
          draggable={false}
          className={cn("object-contain", small ? "size-5" : "size-6")}
        />
      ) : (
        <Webhook className={small ? "size-4" : "size-5"} />
      )}
    </span>
  );
}
export const fieldClass =
  "w-full rounded-md border border-input bg-background px-3 py-2 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50";
export function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <label className="block space-y-2">
      <span className="text-xs font-medium">{label}</span>
      {children}
      {hint && (
        <span className="block text-[11px] leading-relaxed text-muted-foreground">
          {hint}
        </span>
      )}
    </label>
  );
}
export function Section({
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
        <h3 className="text-sm font-medium">{title}</h3>
        {description && (
          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
            {description}
          </p>
        )}
      </div>
      {children}
    </section>
  );
}
export function Choice({
  label,
  description,
  checked,
  onChange,
}: {
  label: string;
  description?: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label
      className={cn(
        "flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors",
        checked
          ? "border-primary/30 bg-primary/5"
          : "border-border hover:bg-surface-2/50",
      )}
    >
      <Checkbox
        className="mt-0.5 shrink-0"
        checked={checked}
        onCheckedChange={(value) => onChange(value === true)}
      />
      <span className="min-w-0">
        <span className="block break-words text-xs font-medium">{label}</span>
        {description && (
          <span className="mt-1 block text-[11px] leading-relaxed text-muted-foreground">
            {description}
          </span>
        )}
      </span>
    </label>
  );
}
