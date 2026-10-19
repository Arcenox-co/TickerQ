import { useState, type FormEvent } from "react";
import { Activity, Pencil, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { DataCard } from "@/components/cron/PageHeader";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { cn } from "@/lib/utils";
import type {
  IntegrationConfiguration,
  IntegrationMonitor,
  IntegrationMonitorStatus,
} from "@/services/integration-types";
import { EditorShell } from "./EditorShell";
import { FunctionPicker } from "./FunctionPicker";
import { Choice, Field, Section, fieldClass } from "./Fields";
import { errorText, providerName } from "./configuration";

const labels = {
  deadline: "Daily deadline",
  long_running: "Long-running execution",
  consecutive_failures: "Repeated failures",
};
export function MonitoringPanel({
  config,
  statuses,
  available,
  pending,
  intervalSeconds,
  busy,
  onSave,
}: {
  config: IntegrationConfiguration;
  statuses: IntegrationMonitorStatus[];
  available: boolean;
  pending: number;
  intervalSeconds: number;
  busy: boolean;
  onSave: (config: IntegrationConfiguration, message: string) => Promise<void>;
}) {
  const [editor, setEditor] = useState<{
    rule: IntegrationMonitor;
    base: IntegrationConfiguration;
    isNew: boolean;
  } | null>(null);
  const [remove, setRemove] = useState<IntegrationMonitor | null>(null);
  const [error, setError] = useState<string | null>(null);
  const monitors = config.monitors ?? [];
  function create() {
    setEditor({
      isNew: true,
      base: structuredClone(config),
      rule: {
        id: crypto.randomUUID(),
        name: "",
        enabled: true,
        function: "",
        kind: "deadline",
        destinationIds:
          config.destinations.length === 1 ? [config.destinations[0].id] : [],
        notifyRecovery: true,
        timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
        deadlineLocalTime: "08:00",
        graceMinutes: 10,
        durationSeconds: 300,
        failureThreshold: 3,
        lookbackHours: 24,
      },
    });
  }
  if (!available)
    return (
      <DataCard className="p-5 text-sm text-muted-foreground">
        This operational store does not support monitoring. Update the storage
        provider to a matching TickerQ version.
      </DataCard>
    );
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">
            Know when important work needs attention
          </h2>
          <p className="max-w-2xl text-xs leading-relaxed text-muted-foreground">
            Monitor individual function executions, including chain steps.
            Checks run approximately every {intervalSeconds} seconds.{" "}
            {pending > 0 ? `${pending} alert deliveries are pending.` : ""}
          </p>
        </div>
        <Button
          size="sm"
          disabled={
            busy || monitors.length >= 20 || !config.destinations.length
          }
          onClick={create}
        >
          <Plus className="size-3.5" />
          Create monitor
        </Button>
      </div>
      {error && (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      )}
      {monitors.length === 0 ? (
        <DataCard className="space-y-2 p-6 text-center">
          <Activity className="mx-auto size-6 text-muted-foreground" />
          <h3 className="text-sm font-medium">
            Catch missed deadlines and unhealthy jobs
          </h3>
          <p className="text-xs text-muted-foreground">
            {config.destinations.length
              ? "Choose a function, an alert condition, and where to notify your team."
              : "Add a connection first, then choose which jobs to monitor."}
          </p>
        </DataCard>
      ) : (
        <div className="grid gap-3 xl:grid-cols-2">
          {monitors.map((rule) => {
            const current = statuses.find((s) => s.monitorId === rule.id);
            const status = !rule.enabled
              ? "paused"
              : (current?.status ?? "waiting");
            return (
              <DataCard key={rule.id} className="min-w-0 p-4">
                <div className="flex flex-col items-start gap-2 sm:flex-row sm:justify-between sm:gap-3">
                  <div className="min-w-0 space-y-1">
                    <h3 className="break-words text-sm font-medium">
                      {rule.name}
                    </h3>
                    <p className="break-all font-mono text-[11px] text-muted-foreground">
                      {rule.function}
                    </p>
                  </div>
                  <span
                    className={cn(
                      "shrink-0 rounded-full px-2 py-1 text-[10px] font-medium capitalize",
                      status === "incident"
                        ? "bg-destructive/10 text-destructive"
                        : status === "healthy"
                          ? "bg-status-healthy/10 text-status-healthy"
                          : "bg-surface-2 text-muted-foreground",
                    )}
                  >
                    {status === "incident" ? "Needs attention" : status}
                  </span>
                </div>
                <div className="mt-3 rounded-md bg-surface-1 p-3 text-xs">
                  <p className="font-medium">{labels[rule.kind]}</p>
                  <p className="mt-1 text-muted-foreground">
                    {rule.kind === "deadline"
                      ? `${rule.deadlineLocalTime} · ${rule.timeZoneId} · ${rule.graceMinutes} min grace`
                      : rule.kind === "long_running"
                        ? `Execution duration of ${rule.durationSeconds} seconds or longer`
                        : `${rule.failureThreshold} consecutive failures within ${rule.lookbackHours} hours`}
                  </p>
                </div>
                <p className="mt-3 text-xs leading-relaxed text-muted-foreground">
                  {!rule.enabled
                    ? "This monitor is paused."
                    : (current?.message ?? "Waiting for the first check.")}
                </p>
                <p className="mt-2 text-[11px] text-muted-foreground">
                  Send to:{" "}
                  {rule.destinationIds
                    .map(
                      (id) =>
                        config.destinations.find((d) => d.id === id)?.name ??
                        "Removed connection",
                    )
                    .join(", ")}
                  {rule.notifyRecovery ? " · Recovery alerts on" : ""}
                </p>
                <div className="mt-4 flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3">
                  <span className="text-[10px] text-muted-foreground">
                    {current?.lastCheckedUtc
                      ? `Checked ${new Date(current.lastCheckedUtc).toLocaleString()}`
                      : "Not checked yet"}
                  </span>
                  <div className="flex gap-1">
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={busy}
                      aria-label={`Edit monitor ${rule.name}`}
                      onClick={() =>
                        setEditor({
                          rule: structuredClone(rule),
                          base: structuredClone(config),
                          isNew: false,
                        })
                      }
                    >
                      <Pencil className="size-3.5" />
                      Edit
                    </Button>
                    <Button
                      size="icon"
                      variant="ghost"
                      disabled={busy}
                      aria-label={`Remove monitor ${rule.name}`}
                      onClick={() => setRemove(rule)}
                    >
                      <Trash2 className="size-3.5" />
                    </Button>
                  </div>
                </div>
              </DataCard>
            );
          })}
        </div>
      )}
      {editor && (
        <MonitorEditor
          key={editor.rule.id}
          initial={editor.rule}
          isNew={editor.isNew}
          config={editor.base}
          onClose={() => setEditor(null)}
          onSave={async (rule) => {
            const previous = editor.base.monitors ?? [];
            await onSave(
              {
                ...editor.base,
                monitors: editor.isNew
                  ? [...previous, rule]
                  : previous.map((r) => (r.id === rule.id ? rule : r)),
              },
              "Monitor saved",
            );
            setEditor(null);
          }}
        />
      )}
      <ConfirmDialog
        open={remove !== null}
        onOpenChange={(open) => {
          if (!open) setRemove(null);
        }}
        title={`Remove ${remove?.name ?? "monitor"}?`}
        description="This monitor will stop checking the function. Pending deliveries for this monitor will be discarded; an in-flight request may finish. Your connections and other rules stay unchanged."
        confirmLabel="Remove monitor"
        confirmVariant="destructive"
        isPending={busy}
        onConfirm={async () => {
          if (!remove) return;
          try {
            await onSave(
              {
                ...config,
                monitors: monitors.filter((r) => r.id !== remove.id),
              },
              "Monitor removed",
            );
            setRemove(null);
            setError(null);
          } catch (e) {
            setError(errorText(e));
            setRemove(null);
          }
        }}
      />
    </div>
  );
}

function MonitorEditor({
  initial,
  isNew,
  config,
  onClose,
  onSave,
}: {
  initial: IntegrationMonitor;
  isNew: boolean;
  config: IntegrationConfiguration;
  onClose: () => void;
  onSave: (rule: IntegrationMonitor) => Promise<void>;
}) {
  const [draft, setDraft] = useState(() => structuredClone(initial));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const zones = [
    "UTC",
    ...(typeof Intl.supportedValuesOf === "function"
      ? Intl.supportedValuesOf("timeZone")
      : []),
  ];
  function patch(value: Partial<IntegrationMonitor>) {
    setDraft((d) => ({ ...d, ...value }));
    setError(null);
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (
      !draft.name.trim() ||
      !draft.function.trim() ||
      !draft.destinationIds.length
    ) {
      setError(
        "Enter a name, choose one function, and select at least one connection.",
      );
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await onSave({
        ...draft,
        name: draft.name.trim(),
        function: draft.function.trim(),
      });
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <EditorShell
      title={isNew ? "Create monitor" : "Edit monitor"}
      description="Set an expectation for one function and notify your team when it needs attention."
      icon={<Activity className="size-6 text-primary" />}
      dirty={isNew || JSON.stringify(initial) !== JSON.stringify(draft)}
      busy={busy}
      error={error}
      saveLabel={isNew ? "Create monitor" : "Save changes"}
      onClose={onClose}
      onSubmit={(event) => void submit(event)}
    >
      <Field label="Monitor name">
        <Input
          autoFocus
          required
          maxLength={80}
          placeholder="e.g. Daily invoice deadline"
          value={draft.name}
          onChange={(e) => patch({ name: e.target.value })}
        />
      </Field>
      <Field
        label="Function"
        hint="One exact function name. Chain steps are checked individually; a parent's success does not mean the whole chain finished."
      >
        <FunctionPicker
          value={draft.function}
          onChange={(name) => patch({ function: name })}
        />
      </Field>
      <Field label="Alert condition">
        <select
          className={fieldClass}
          value={draft.kind}
          onChange={(e) =>
            patch({ kind: e.target.value as IntegrationMonitor["kind"] })
          }
        >
          {Object.entries(labels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </Field>
      {draft.kind === "deadline" && (
        <Section
          title="Daily completion deadline"
          description="Requires a successful execution since local midnight. New or edited monitors begin with their next deadline; grace delays the alert. A later success sends recovery if enabled."
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Finish by">
              <Input
                type="time"
                className="dark:[color-scheme:dark]"
                required
                value={draft.deadlineLocalTime}
                onChange={(e) => patch({ deadlineLocalTime: e.target.value })}
              />
            </Field>
            <Field label="Grace period (minutes)">
              <Input
                type="number"
                min={0}
                max={720}
                required
                value={draft.graceMinutes}
                onChange={(e) =>
                  patch({ graceMinutes: Number(e.target.value) })
                }
              />
            </Field>
          </div>
          <Field
            label="Deadline time zone"
            hint="This time zone belongs to the rule and is independent of the dashboard display time zone."
          >
            <Input
              required
              list="monitor-timezones"
              value={draft.timeZoneId}
              onChange={(e) => patch({ timeZoneId: e.target.value })}
            />
            <datalist id="monitor-timezones">
              {zones.map((z) => (
                <option key={z} value={z} />
              ))}
            </datalist>
          </Field>
          <p className="text-[11px] leading-relaxed text-muted-foreground">
            Runs every day. On daylight-saving changes, skipped times move to
            the next valid minute; repeated times use the later occurrence.
          </p>
        </Section>
      )}
      {draft.kind === "long_running" && (
        <Section
          title="Duration threshold"
          description="Active runs are timed from the first check that sees InProgress. Completed runs use their recorded duration, so slow jobs that finish between checks are included. Retry waits count; queued time is excluded. This monitor does not cancel jobs."
        >
          <Field label="Alert after (seconds)">
            <Input
              required
              type="number"
              min={5}
              max={604800}
              value={draft.durationSeconds}
              onChange={(e) =>
                patch({ durationSeconds: Number(e.target.value) })
              }
            />
          </Field>
        </Section>
      )}
      {draft.kind === "consecutive_failures" && (
        <Section
          title="Failure threshold"
          description="Counts final failed executions after their retries. Successful or cancelled executions break the streak; skipped executions are ignored."
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Consecutive failures">
              <Input
                required
                type="number"
                min={2}
                max={20}
                value={draft.failureThreshold}
                onChange={(e) =>
                  patch({ failureThreshold: Number(e.target.value) })
                }
              />
            </Field>
            <Field label="Lookback (hours)">
              <Input
                required
                type="number"
                min={1}
                max={168}
                value={draft.lookbackHours}
                onChange={(e) =>
                  patch({ lookbackHours: Number(e.target.value) })
                }
              />
            </Field>
          </div>
        </Section>
      )}
      <Section title="Send to">
        <div className="grid gap-2 sm:grid-cols-2">
          {config.destinations.map((d) => (
            <Choice
              key={d.id}
              label={d.name}
              description={`${providerName(d.provider)}${d.enabled ? "" : " · Paused"}`}
              checked={draft.destinationIds.includes(d.id)}
              onChange={(checked) =>
                patch({
                  destinationIds: checked
                    ? [...draft.destinationIds, d.id]
                    : draft.destinationIds.filter((id) => id !== d.id),
                })
              }
            />
          ))}
        </div>
        {draft.destinationIds.length > 0 &&
          config.destinations
            .filter((d) => draft.destinationIds.includes(d.id))
            .every((d) => !d.enabled) && (
            <p className="text-xs text-amber-600">
              All selected connections are paused. Enable a connection to
              receive alerts.
            </p>
          )}
      </Section>
      <label className="flex items-center justify-between gap-4 rounded-lg border border-border p-3">
        <span className="text-xs">
          Send a recovery message after a successful execution
        </span>
        <Switch
          checked={draft.notifyRecovery}
          onCheckedChange={(value) => patch({ notifyRecovery: value })}
        />
      </label>
      <label className="flex items-center justify-between gap-4">
        <span className="text-xs">Monitor enabled</span>
        <Switch
          checked={draft.enabled}
          onCheckedChange={(value) => patch({ enabled: value })}
        />
      </label>
      <p className="text-[11px] leading-relaxed text-muted-foreground">
        Keep execution history for at least the monitoring window. Missing
        history cannot prove a job succeeded. Changes restart this monitor's
        observation window.
      </p>
    </EditorShell>
  );
}
