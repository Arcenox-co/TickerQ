import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, Eye, Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { PageHeader, DataCard, DataCardHeader } from "@/components/cron/PageHeader";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { SchedulePolicyEditorDialog } from "@/components/cron/SchedulePolicyEditorDialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { dashboardApi } from "@/services/dashboard-api";
import { qk, useSchedulePolicies } from "@/services/hooks";
import type { SchedulePolicy, SchedulePolicyPreviewItem } from "@/services/api-types";
import { isReadOnly } from "@/lib/runtime-config";

const weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"] as const;

function expandedWeekdays(value: string) {
  if (value === "All") return [...weekdays];
  if (value === "MondayToFriday") return weekdays.slice(0, 5);
  if (value === "Weekend") return weekdays.slice(5);
  return value.split(",").map(x => x.trim()).filter((x): x is typeof weekdays[number] =>
    weekdays.includes(x as typeof weekdays[number]));
}

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

    <div className="grid items-start gap-4 lg:grid-cols-2">
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
    <PolicyPreview open={!!previewing} policy={previewing} onClose={() => setPreviewing(null)} />
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
  const windows = policy.allowedTimeWindows.length === 0 ? "All day" : policy.allowedTimeWindows
    .map(x => `${timeValue(x.start)}–${timeValue(x.end)}`).join(", ");
  return <DataCard>
    <DataCardHeader title={policy.name}>
      <Badge variant="outline" className="font-normal">v{policy.version}</Badge>
    </DataCardHeader>
    <div className="space-y-3 border-t border-border p-4 text-xs">
      {policy.description && <p className="text-muted-foreground">{policy.description}</p>}
      <dl className="grid gap-3 sm:grid-cols-2">
        <PolicyMeta label="Time zone" value={policy.timeZoneId} />
        <PolicyMeta label="Allowed days" value={expandedWeekdays(policy.allowedWeekdays).join(", ")} />
        <PolicyMeta label="Daily windows" value={windows} />
        <PolicyMeta label="Date exclusions" value={`${policy.excludedDates.length} exact · ${policy.excludedAnnualDates.length} annual`} />
      </dl>
      {(policy.requiredPolicyNames.length > 0 || policy.excludedWhenPolicyAllowsNames.length > 0) &&
        <div className="flex flex-wrap gap-1.5">
          {policy.requiredPolicyNames.map(x => <Badge key={`r-${x}`} variant="secondary">Requires {x}</Badge>)}
          {policy.excludedWhenPolicyAllowsNames.map(x => <Badge key={`e-${x}`} variant="outline">Except {x}</Badge>)}
        </div>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-border pt-3">
        <Button variant="outline" size="sm" onClick={onPreview}><Eye className="mr-1.5 size-3.5" /> Preview</Button>
        {!readOnly && <Button variant="outline" size="sm" onClick={onEdit}><Pencil className="mr-1.5 size-3.5" /> Edit</Button>}
        {!readOnly && <Button variant="ghost" size="sm" className="text-status-error" onClick={onDelete}><Trash2 className="mr-1.5 size-3.5" /> Delete</Button>}
      </div>
    </div>
  </DataCard>;
}

function PolicyMeta({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-muted-foreground">{label}</dt><dd className="mt-0.5 text-foreground">{value}</dd></div>;
}

function PolicyPreview({ open, policy, onClose }: { open: boolean; policy: SchedulePolicy | null; onClose: () => void }) {
  const [expression, setExpression] = useState("0 9 * * *");
  const [items, setItems] = useState<SchedulePolicyPreviewItem[]>([]);
  const preview = useMutation({
    mutationFn: () => dashboardApi.previewSchedulePolicy(policy!.id, {
      expression, fromUtc: new Date().toISOString(), count: 20, timeZoneId: policy!.timeZoneId,
    }),
    onSuccess: setItems,
    onError: error => toast.error("Preview failed", { description: error instanceof Error ? error.message : "Unknown error" }),
  });
  return <Dialog open={open} onOpenChange={next => !next && onClose()}>
    <DialogContent className="max-w-3xl">
      <DialogHeader><DialogTitle>Preview {policy?.name}</DialogTitle><DialogDescription>Evaluate the next 20 nominal cron occurrences against this policy.</DialogDescription></DialogHeader>
      <div className="flex gap-2"><Input value={expression} onChange={e => setExpression(e.target.value)} placeholder="0 9 * * *" /><Button onClick={() => preview.mutate()} disabled={!policy || preview.isPending}>{preview.isPending ? "Evaluating…" : "Preview"}</Button></div>
      <div className="max-h-96 overflow-y-auto rounded-md border border-border">
        {items.length === 0 ? <p className="p-6 text-center text-xs text-muted-foreground">Run a preview to see allowed and blocked occurrences.</p> :
          <div className="divide-y divide-border">{items.map((item, index) => <div key={`${item.requestedAtUtc}-${index}`} className="grid gap-1 p-3 text-xs sm:grid-cols-[190px_80px_1fr]">
            <span className="font-mono">{new Date(item.requestedAtUtc).toLocaleString()}</span>
            <Badge variant={item.isAllowed ? "secondary" : "outline"} className="w-fit">{item.isAllowed ? "Allowed" : "Blocked"}</Badge>
            <span className="text-muted-foreground">{item.reason}</span>
          </div>)}</div>}
      </div>
      <DialogFooter><Button variant="outline" onClick={onClose}>Close</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}
