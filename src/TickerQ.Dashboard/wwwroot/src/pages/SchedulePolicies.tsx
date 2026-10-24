import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Ban, CalendarClock, CheckCircle2, Eye, Globe2, Pencil, Play, Plus, Trash2, XCircle } from "lucide-react";
import { toast } from "sonner";

import { PageHeader, DataCard } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { CronExpressionPreview } from "@/components/cron/CronExpressionPreview";
import { SchedulePolicyEditorDialog } from "@/components/cron/SchedulePolicyEditorDialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { dashboardApi } from "@/services/dashboard-api";
import { qk, useSchedulePolicies } from "@/services/hooks";
import type { SchedulePolicy, SchedulePolicyPreviewItem } from "@/services/api-types";
import { isReadOnly } from "@/lib/runtime-config";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { expandWeekdays, weekdays } from "@/lib/schedule-weekdays";

const cronPresets = [
  { label: "Every 15 min", expression: "*/15 * * * *" },
  { label: "Hourly", expression: "0 * * * *" },
  { label: "Daily 09:00", expression: "0 9 * * *" },
  { label: "Weekdays 18:00", expression: "0 18 * * 1-5" },
  { label: "Sundays 03:00", expression: "0 3 * * 0" },
];

function timeValue(value: string) {
  return value?.slice(0, 5) || "09:00";
}

export default function SchedulePoliciesPage() {
  const query = useSchedulePolicies();
  const queryClient = useQueryClient();
  const policies = query.data ?? [];
  const [editing, setEditing] = useState<SchedulePolicy | null | undefined>(undefined);
  const [previewing, setPreviewing] = useState<SchedulePolicy | null>(null);
  const [deleting, setDeleting] = useState<SchedulePolicy | null>(null);
  const readOnly = isReadOnly();

  const remove = useMutation({
    mutationFn: (policy: SchedulePolicy) => dashboardApi.deleteSchedulePolicy(policy.id, policy.version),
    onSuccess: async () => {
      toast.success("Schedule policy deleted");
      setDeleting(null);
      await queryClient.invalidateQueries({ queryKey: qk.schedulePolicies });
    },
    onError: error => toast.error("Could not delete policy", { description: error instanceof Error ? error.message : "Unknown error" }),
  });

  return <div className="mx-auto max-w-7xl space-y-5">
    <PageHeader title="Schedule Policies" count={policies.length}
      description="Reusable availability rules for recurring schedules, one-time jobs, and chain roots.">
      {!readOnly && <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1.5 size-3.5" /> New policy</Button>}
    </PageHeader>

    {query.isError && <QueryErrorNotice message="Schedule policies could not be loaded." onRetry={() => void query.refetch()} />}

    <DataCard className="px-4 py-3 text-xs leading-relaxed text-muted-foreground">
      Policies evaluate candidate times in their own time zone. Cron occurrences outside a policy are skipped. One-time jobs can reject, skip, or move to the next allowed instant. Child steps continue after a chain root is admitted.
    </DataCard>

    {!query.isPending && policies.length === 0 && <DataCard className="p-10 text-center">
      <CalendarClock className="mx-auto size-7 text-muted-foreground" />
      <p className="mt-3 text-sm font-medium">No schedule policies yet</p>
      <p className="mt-1 text-xs text-muted-foreground">Create one for business hours, maintenance windows, holidays, or blackout dates.</p>
    </DataCard>}

    {/* auto-rows-fr gives every card the height of the tallest one, across rows too. */}
    <div className="grid auto-rows-fr gap-4 lg:grid-cols-2">
      {policies.map(policy => <PolicyCard key={policy.id} policy={policy}
        onEdit={() => setEditing(policy)} onPreview={() => setPreviewing(policy)}
        onDelete={() => setDeleting(policy)} readOnly={readOnly} />)}
    </div>

    <SchedulePolicyEditorDialog
      open={editing !== undefined}
      policy={editing ?? null}
      policies={policies}
      onClose={() => setEditing(undefined)}
    />
    {/* Keyed by policy so each preview starts with fresh results. */}
    <PolicyPreview key={previewing?.id ?? "closed"} open={!!previewing} policy={previewing} onClose={() => setPreviewing(null)} />
    <ConfirmDialog open={!!deleting} onOpenChange={open => { if (!open) setDeleting(null); }}
      title="Delete schedule policy?"
      description="Deletion is blocked while any ticker or composed policy still references it."
      confirmLabel="Delete policy" confirmVariant="destructive" isPending={remove.isPending}
      onConfirm={() => { if (deleting) remove.mutate(deleting); }} />
  </div>;
}

function PolicyCard({ policy, onEdit, onPreview, onDelete, readOnly }: {
  policy: SchedulePolicy; onEdit: () => void; onPreview: () => void; onDelete: () => void; readOnly: boolean;
}) {
  const days = expandWeekdays(policy.allowedWeekdays);
  const windows = policy.allowedTimeWindows.length === 0 ? "All day" : policy.allowedTimeWindows
    .map(x => `${timeValue(x.start)}–${timeValue(x.end)}`).join(", ");
  const exclusions = policy.excludedDates.length + policy.excludedAnnualDates.length;
  const composed = policy.requiredPolicyNames.length + policy.excludedWhenPolicyAllowsNames.length;

  return <DataCard className="flex h-full min-w-0 flex-col">
    <div className="flex items-start justify-between gap-3 border-b border-border px-4 py-3">
      <div className="min-w-0">
        <h3 className="truncate text-sm font-medium" title={policy.name}>{policy.name}</h3>
        <p className="mt-0.5 flex items-center gap-1 text-[11px] text-muted-foreground">
          <Globe2 className="size-3 shrink-0" /><span className="truncate">{policy.timeZoneId}</span>
        </p>
      </div>
      <Badge variant="outline" className="shrink-0 font-normal">v{policy.version}</Badge>
    </div>

    <div className="flex flex-1 flex-col gap-4 p-4 text-xs">
      {/* Fixed two-line slot so short and long descriptions take the same space. */}
      <p className={cn("line-clamp-2 min-h-[2lh] leading-relaxed", policy.description ? "text-muted-foreground" : "italic text-muted-foreground/60")}
        title={policy.description ?? undefined}>
        {policy.description || "No description"}
      </p>

      <div>
        <p className="mb-1.5 text-muted-foreground">Allowed days</p>
        <div className="grid grid-cols-7 gap-1">
          {weekdays.map(day => {
            const on = days.includes(day);
            return <span key={day} title={`${day}: ${on ? "allowed" : "blocked"}`}
              className={cn("rounded-md border py-1 text-center text-[10.5px] font-medium",
                on ? "border-primary/40 bg-primary/10 text-primary" : "border-border text-muted-foreground/50 line-through")}>
              {day.slice(0, 3)}
            </span>;
          })}
        </div>
      </div>

      <dl className="grid grid-cols-2 gap-3">
        <PolicyMeta label="Daily windows" value={windows} />
        <PolicyMeta label="Date exclusions"
          value={exclusions === 0 ? "None" : `${policy.excludedDates.length} exact · ${policy.excludedAnnualDates.length} annual`} />
      </dl>

      <div className="min-h-6">
        {composed === 0 ? <p className="text-muted-foreground/60">Not combined with other policies</p> :
          <div className="flex flex-wrap gap-1.5">
            {policy.requiredPolicyNames.map(x => <span key={`r-${x}`}
              className="inline-flex items-center gap-1 rounded-full border border-status-healthy/40 bg-status-healthy/10 px-2 py-0.5 text-[10.5px] font-medium text-status-healthy">
              <CheckCircle2 className="size-3" /> Requires {x}
            </span>)}
            {policy.excludedWhenPolicyAllowsNames.map(x => <span key={`e-${x}`}
              className="inline-flex items-center gap-1 rounded-full border border-status-error/40 bg-status-error/10 px-2 py-0.5 text-[10.5px] font-medium text-status-error">
              <Ban className="size-3" /> Except {x}
            </span>)}
          </div>}
      </div>

      <div className="mt-auto flex flex-wrap justify-end gap-2 border-t border-border pt-3">
        <Button variant="outline" size="sm" onClick={onPreview}><Eye className="mr-1.5 size-3.5" /> Preview</Button>
        {!readOnly && <Button variant="outline" size="sm" onClick={onEdit}><Pencil className="mr-1.5 size-3.5" /> Edit</Button>}
        {!readOnly && <Button variant="ghost" size="sm" className="text-status-error" onClick={onDelete}><Trash2 className="mr-1.5 size-3.5" /> Delete</Button>}
      </div>
    </div>
  </DataCard>;
}

function PolicyMeta({ label, value }: { label: string; value: string }) {
  return <div className="min-w-0"><dt className="text-muted-foreground">{label}</dt><dd className="mt-0.5 truncate text-foreground" title={value}>{value}</dd></div>;
}

/** Formats an instant in an IANA zone; returns null when the browser does not know the zone. */
function formatIn(value: string, timeZone: string) {
  try {
    const date = new Date(value);
    return {
      day: new Intl.DateTimeFormat(undefined, { timeZone, weekday: "short", day: "2-digit", month: "short", year: "numeric" }).format(date),
      time: new Intl.DateTimeFormat(undefined, { timeZone, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(date),
    };
  } catch {
    return null;
  }
}

function PolicyPreview({ open, policy, onClose }: { open: boolean; policy: SchedulePolicy | null; onClose: () => void }) {
  const { timezone } = useTimezone();
  const [expression, setExpression] = useState("0 9 * * *");
  const [items, setItems] = useState<SchedulePolicyPreviewItem[]>([]);
  const preview = useMutation({
    mutationFn: ({ target, cron }: { target: SchedulePolicy; cron: string }) => dashboardApi.previewSchedulePolicy(target.id, {
      expression: cron, fromUtc: new Date().toISOString(), count: 20, timeZoneId: target.timeZoneId,
    }),
    onSuccess: setItems,
    onError: error => toast.error("Preview failed", { description: error instanceof Error ? error.message : "Unknown error" }),
  });
  const { mutate, isPending } = preview;

  // Evaluate the current expression as soon as the dialog opens for a policy.
  useEffect(() => {
    if (!open || !policy) return;
    mutate({ target: policy, cron: expression });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- run once per opened policy, not on every keystroke
  }, [open, policy?.id, mutate]);

  const run = (cron = expression) => {
    if (!policy || !cron.trim()) return;
    setExpression(cron);
    mutate({ target: policy, cron });
  };

  const allowed = items.filter(x => x.isAllowed).length;
  const blocked = items.length - allowed;
  const policyZone = policy?.timeZoneId ?? "UTC";
  // Only show the viewer's time when it differs from the policy time for some occurrence.
  const showLocal = items.some(item => {
    const inPolicy = formatIn(item.requestedAtUtc, policyZone);
    const local = formatIn(item.requestedAtUtc, timezone);
    return !inPolicy || !local || inPolicy.day !== local.day || inPolicy.time !== local.time;
  });

  return <Dialog open={open} onOpenChange={next => !next && onClose()}>
    <DialogContent className="flex max-h-[92vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-4xl">
      <DialogHeader className="shrink-0 border-b border-border/70 px-6 pb-4 pt-5">
        <DialogTitle className="flex items-center gap-2"><CalendarClock className="size-4 text-primary" />Preview {policy?.name}</DialogTitle>
        <DialogDescription className="text-xs">
          Checks the next 20 occurrences of a cron expression against this policy. Times are evaluated in {policyZone}.
        </DialogDescription>
      </DialogHeader>

      <div className="shrink-0 space-y-3 border-b border-border/70 px-6 py-4">
        <Label htmlFor="policy-preview-cron" className="text-[10px] uppercase tracking-wider text-muted-foreground">Cron expression</Label>
        <form className="flex flex-col gap-2 sm:flex-row" onSubmit={event => { event.preventDefault(); run(); }}>
          <Input id="policy-preview-cron" value={expression} onChange={e => setExpression(e.target.value)}
            placeholder="0 9 * * *" className="h-10 flex-1 font-mono text-sm" autoComplete="off" spellCheck={false} />
          <Button type="submit" className="h-10 sm:w-36" disabled={!policy || isPending || !expression.trim()}>
            <Play className="mr-1.5 size-3.5" />{isPending ? "Evaluating…" : "Run preview"}
          </Button>
        </form>
        <CronExpressionPreview expression={expression} />
        <div className="flex flex-wrap gap-1.5">
          {cronPresets.map(preset => <button key={preset.expression} type="button" onClick={() => run(preset.expression)}
            className={cn("rounded-full border px-2.5 py-1 text-[11px] transition-colors",
              expression.trim() === preset.expression
                ? "border-primary bg-primary text-primary-foreground"
                : "border-border text-muted-foreground hover:border-primary/50 hover:text-foreground")}>
            {preset.label}
          </button>)}
        </div>
      </div>

      {items.length > 0 && <div className="grid shrink-0 grid-cols-3 gap-3 px-6 pt-4">
        <PreviewStat label="Occurrences" value={items.length} />
        <PreviewStat label="Allowed" value={allowed} tone="allowed" />
        <PreviewStat label="Blocked" value={blocked} tone="blocked" />
      </div>}

      <div className="min-h-0 flex-1 overflow-auto px-6 py-4 scrollbar-thin">
        {items.length === 0 ? <div className="rounded-lg border border-dashed border-border p-10 text-center text-xs text-muted-foreground">
          {isPending ? "Evaluating occurrences…" : "Run a preview to see which occurrences this policy allows or blocks."}
        </div> :
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full min-w-[640px] text-xs">
              <thead className="sticky top-0 bg-muted/60 text-left text-[10px] uppercase tracking-wider text-muted-foreground backdrop-blur">
                <tr>
                  <th className="w-10 px-3 py-2 font-medium">#</th>
                  <th className="px-3 py-2 font-medium">Policy time ({policyZone})</th>
                  {showLocal && <th className="px-3 py-2 font-medium">Your time ({timezone})</th>}
                  <th className="w-28 px-3 py-2 font-medium">Result</th>
                  <th className="px-3 py-2 font-medium">Reason</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {items.map((item, index) => {
                  const inPolicy = formatIn(item.requestedAtUtc, policyZone);
                  const local = formatIn(item.requestedAtUtc, timezone);
                  return <tr key={`${item.requestedAtUtc}-${index}`}
                    className={cn("border-l-2", item.isAllowed ? "border-l-status-healthy" : "border-l-status-error bg-status-error/[0.04]")}>
                    <td className="px-3 py-2.5 tabular-nums text-muted-foreground">{index + 1}</td>
                    <td className="whitespace-nowrap px-3 py-2.5">
                      {inPolicy ? <><span className="font-medium">{inPolicy.day}</span><span className="ml-2 font-mono text-muted-foreground">{inPolicy.time}</span></>
                        : <span className="font-mono">{new Date(item.requestedAtUtc).toISOString()}</span>}
                    </td>
                    {showLocal && <td className="whitespace-nowrap px-3 py-2.5 text-muted-foreground">
                      {local ? <>{local.day}<span className="ml-2 font-mono">{local.time}</span></> : "—"}
                    </td>}
                    <td className="whitespace-nowrap px-3 py-2.5">
                      {item.isAllowed
                        ? <span className="inline-flex items-center gap-1 rounded-full bg-status-healthy/12 px-2 py-0.5 font-medium text-status-healthy"><CheckCircle2 className="size-3.5" />Allowed</span>
                        : <span className="inline-flex items-center gap-1 rounded-full bg-status-error/12 px-2 py-0.5 font-medium text-status-error"><XCircle className="size-3.5" />Blocked</span>}
                    </td>
                    <td className="px-3 py-2.5 text-muted-foreground">{item.reason || "—"}</td>
                  </tr>;
                })}
              </tbody>
            </table>
          </div>}
      </div>

      <DialogFooter className="m-0 shrink-0 rounded-none rounded-b-xl border-t border-border/70 bg-muted/30 px-6 py-3">
        <Button variant="outline" onClick={onClose}>Close</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function PreviewStat({ label, value, tone }: { label: string; value: number; tone?: "allowed" | "blocked" }) {
  return <div className={cn("rounded-lg border px-3 py-2",
    tone === "allowed" ? "border-status-healthy/30 bg-status-healthy/[0.06]"
      : tone === "blocked" ? "border-status-error/30 bg-status-error/[0.06]" : "border-border")}>
    <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
    <p className={cn("mt-0.5 text-lg font-semibold tabular-nums",
      tone === "allowed" ? "text-status-healthy" : tone === "blocked" ? "text-status-error" : "text-foreground")}>{value}</p>
  </div>;
}
