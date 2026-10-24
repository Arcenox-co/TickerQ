import type { ReactNode } from "react";
import { Ban, CheckCircle2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { expandWeekdays, weekdays } from "@/lib/schedule-weekdays";
import type { SaveSchedulePolicyRequest } from "@/services/api-types";

const hours = (policy: SaveSchedulePolicyRequest) => policy.allowedTimeWindows.length === 0
  ? "All day"
  : policy.allowedTimeWindows.map((window) => `${window.start.slice(0, 5)}–${window.end.slice(0, 5)}`).join(", ");
const exactDates = (policy: SaveSchedulePolicyRequest) => policy.excludedDates.join(", ") || "None";
const yearlyDates = (policy: SaveSchedulePolicyRequest) => policy.excludedAnnualDates
  .map((date) => `${String(date.month).padStart(2, "0")}-${String(date.day).padStart(2, "0")}`).join(", ") || "None";
const days = (policy: SaveSchedulePolicyRequest) => expandWeekdays(policy.allowedWeekdays).join(",");
const names = (values: string[]) => values.join(", ");

/** Summary of a proposed schedule policy. For updates, changed fields show the previous value. */
export function SchedulePolicyProposal({ policy, current }: {
  policy: SaveSchedulePolicyRequest;
  current?: SaveSchedulePolicyRequest | null;
}) {
  const changed = (read: (value: SaveSchedulePolicyRequest) => string) => !!current && read(current) !== read(policy);
  const selected = expandWeekdays(policy.allowedWeekdays);
  const previousDays = current ? expandWeekdays(current.allowedWeekdays) : null;

  return (
    <dl className="space-y-1.5">
      <Field label="Policy"><span className="font-medium">{policy.name}</span></Field>
      <Field label="Time zone" changed={changed((x) => x.timeZoneId)} previous={current?.timeZoneId}>
        <span className="font-mono text-[11.5px]">{policy.timeZoneId}</span>
      </Field>
      <Field label="Days" changed={changed(days)}>
        <div className="grid max-w-xs grid-cols-7 gap-1">
          {weekdays.map((day) => {
            const on = selected.includes(day);
            const was = previousDays?.includes(day);
            return (
              <span
                key={day}
                title={`${day}: ${on ? "allowed" : "blocked"}${previousDays && was !== on ? " (changed)" : ""}`}
                className={cn(
                  "rounded-md border py-0.5 text-center text-[10px] font-medium",
                  on ? "border-primary/40 bg-primary/10 text-primary" : "border-border text-muted-foreground/50 line-through",
                  previousDays && was !== on && "ring-1 ring-status-warning/70",
                )}
              >
                {day.slice(0, 3)}
              </span>
            );
          })}
        </div>
      </Field>
      <Field label="Hours" changed={changed(hours)} previous={current ? hours(current) : undefined}>{hours(policy)}</Field>
      <Field label="Exact dates" changed={changed(exactDates)} previous={current ? exactDates(current) : undefined}>{exactDates(policy)}</Field>
      <Field label="Every year" changed={changed(yearlyDates)} previous={current ? yearlyDates(current) : undefined}>{yearlyDates(policy)}</Field>
      {(policy.requiredPolicyNames.length > 0 || changed((x) => names(x.requiredPolicyNames))) && (
        <Field label="Requires" changed={changed((x) => names(x.requiredPolicyNames))} previous={current ? names(current.requiredPolicyNames) || "None" : undefined}>
          <Chips values={policy.requiredPolicyNames} tone="require" />
        </Field>
      )}
      {(policy.excludedWhenPolicyAllowsNames.length > 0 || changed((x) => names(x.excludedWhenPolicyAllowsNames))) && (
        <Field label="Blocked by" changed={changed((x) => names(x.excludedWhenPolicyAllowsNames))} previous={current ? names(current.excludedWhenPolicyAllowsNames) || "None" : undefined}>
          <Chips values={policy.excludedWhenPolicyAllowsNames} tone="exclude" />
        </Field>
      )}
      {(policy.description || changed((x) => x.description ?? "")) && (
        <Field label="Description" changed={changed((x) => x.description ?? "")} previous={current ? current.description || "None" : undefined}>
          {policy.description || <span className="text-muted-foreground">None</span>}
        </Field>
      )}
    </dl>
  );
}

function Chips({ values, tone }: { values: string[]; tone: "require" | "exclude" }) {
  if (values.length === 0) return <span className="text-muted-foreground">None</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {values.map((value) => (
        <span
          key={value}
          className={cn(
            "inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[10.5px] font-medium",
            tone === "require" ? "border-status-healthy/40 bg-status-healthy/10 text-status-healthy" : "border-status-error/40 bg-status-error/10 text-status-error",
          )}
        >
          {tone === "require" ? <CheckCircle2 className="h-3 w-3" /> : <Ban className="h-3 w-3" />}
          {value}
        </span>
      ))}
    </div>
  );
}

function Field({ label, changed = false, previous, children }: {
  label: string;
  changed?: boolean;
  previous?: string;
  children: ReactNode;
}) {
  return (
    <div className="flex gap-3">
      <dt className="w-16 shrink-0 pt-0.5 text-[10.5px] uppercase tracking-wider text-muted-foreground/60 sm:w-20">{label}</dt>
      <dd className="min-w-0 flex-1 [overflow-wrap:anywhere]">
        {changed && previous !== undefined ? <div className="text-[11px] text-muted-foreground line-through">{previous || "None"}</div> : null}
        <div className="flex items-start gap-2">
          <div className="min-w-0 flex-1">{children}</div>
          {changed ? <span className="shrink-0 rounded-full bg-status-warning/15 px-1.5 py-px text-[9.5px] font-medium text-status-warning">Changed</span> : null}
        </div>
      </dd>
    </div>
  );
}
