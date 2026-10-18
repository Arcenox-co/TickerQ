import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Bell, Plus, Send, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { DataCard, PageHeader } from "@/components/cron/PageHeader";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { dashboardApi } from "@/services/dashboard-api";
import { useAllFunctions } from "@/services/hooks";
import { getRuntimeConfig } from "@/lib/runtime-config";
import type { NotificationConfiguration, NotificationDestination, NotificationRule, NotificationSnapshot } from "@/services/notification-types";

const fieldClass = "w-full rounded-md border border-input bg-background px-3 py-2 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring";
const eventLabels: Record<string, string> = { failed: "Final failure", timeout_cancelled: "Execution timed out", stale_recovered: "Abandoned jobs recovered", contract_drift: "Request contract changed" };
const providerLabel = (name: string) => ({ slack: "Slack", teams: "Teams Workflows", webhook: "Custom webhook" }[name] ?? name);
const errorText = (error: unknown) => error instanceof Error ? error.message : "Please try again.";
const splitPatterns = (value: string) => value.split(/[\n,]/).map(s => s.trim()).filter(Boolean);

export default function NotificationsPage() {
  const enabled = getRuntimeConfig().notificationsEnabled;
  const [tab, setTab] = useState("destinations");
  const query = useQuery({ queryKey: ["notifications"], queryFn: ({ signal }) => dashboardApi.getNotifications(signal), enabled, refetchOnWindowFocus: false });
  if (!enabled) return <div className="space-y-4"><PageHeader title="Notifications" description="Send job alerts to your team's tools." /><DataCard className="p-5 text-sm text-muted-foreground">Enable the optional TickerQ.Notifications package in the host application to configure notifications here.</DataCard></div>;
  if (query.isLoading) return <p className="text-sm text-muted-foreground">Loading notification settings…</p>;
  if (query.isError) return <DataCard className="p-5 space-y-3"><p className="text-sm text-status-error">{errorText(query.error)}</p><Button size="sm" onClick={() => void query.refetch()}>Try again</Button></DataCard>;
  if (!query.data?.canManage) return <DataCard className="p-5 space-y-2"><h1 className="font-semibold">Notifications</h1><p className="text-sm text-muted-foreground">The host must grant notification-management permission, and the dashboard must allow writes, to edit destinations or send tests.</p></DataCard>;
  return <NotificationEditor key={query.data.configuration.version} snapshot={query.data} tab={tab} setTab={setTab} reload={async () => { await query.refetch(); }} />;
}

function NotificationEditor({ snapshot, reload, tab, setTab }: { snapshot: NotificationSnapshot; reload: () => Promise<void>; tab: string; setTab: (tab: string) => void }) {
  const [draft, setDraft] = useState<NotificationConfiguration>(() => structuredClone(snapshot.configuration));
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [headers, setHeaders] = useState<Record<string, string>>({});
  const [preview, setPreview] = useState<{ id: string; payload: string } | null>(null);
  const [confirm, setConfirm] = useState<{ type: "test" | "delete-destination" | "delete-rule" | "reload"; id?: string } | null>(null);
  const functions = useAllFunctions();
  function change(update: (config: NotificationConfiguration) => NotificationConfiguration) { setDraft(update); setDirty(true); setPreview(null); }
  function destination(id: string, patch: Partial<NotificationDestination>) { change(c => ({ ...c, destinations: c.destinations.map(d => d.id === id ? { ...d, ...patch } : d) })); }
  function rule(id: string, patch: Partial<NotificationRule>) { change(c => ({ ...c, rules: c.rules.map(r => r.id === id ? { ...r, ...patch } : r) })); }
  async function save() {
    setBusy(true);
    try {
      const body = structuredClone(draft);
      for (const d of body.destinations) if (headers[d.id] !== undefined) {
        if (!headers[d.id].trim()) d.headers = null;
        else {
          const parsed: unknown = JSON.parse(headers[d.id]);
          if (!parsed || Array.isArray(parsed) || typeof parsed !== "object" || Object.values(parsed).some(v => typeof v !== "string")) throw new Error("Headers must be a JSON object containing string values.");
          d.headers = parsed as Record<string, string>;
        }
      }
      await dashboardApi.saveNotifications(body);
      setDirty(false); setHeaders({}); await reload(); toast.success("Notification settings saved");
    } catch (error) { toast.error("Could not save settings", { description: errorText(error) }); }
    finally { setBusy(false); }
  }
  async function showPreview(d: NotificationDestination) {
    setBusy(true);
    try { const result = await dashboardApi.previewNotification(d.provider, d.payloadTemplate); setPreview({ id: d.id, payload: JSON.stringify(JSON.parse(result.payload), null, 2) }); }
    catch (error) { toast.error("Could not preview payload", { description: errorText(error) }); }
    finally { setBusy(false); }
  }
  async function confirmed() {
    if (!confirm) return;
    if (confirm.type === "delete-destination") change(c => ({ ...c, destinations: c.destinations.filter(d => d.id !== confirm.id), rules: c.rules.map(r => ({ ...r, destinationIds: r.destinationIds.filter(id => id !== confirm.id) })) }));
    if (confirm.type === "delete-rule") change(c => ({ ...c, rules: c.rules.filter(r => r.id !== confirm.id) }));
    if (confirm.type === "reload") { setDraft(structuredClone(snapshot.configuration)); setHeaders({}); setDirty(false); setPreview(null); await reload(); }
    if (confirm.type === "test" && confirm.id) {
      setBusy(true);
      try {
        const result = await dashboardApi.testNotification(confirm.id);
        if (result.outcome === "Sent") toast.success("Test webhook accepted", { description: "Check the destination channel to confirm the message arrived." });
        else toast.error("Test failed", { description: result.message });
        await reload();
      } catch (error) { toast.error("Test failed", { description: errorText(error) }); }
      finally { setBusy(false); }
    }
    setConfirm(null);
  }
  const addDestination = () => change(c => ({ ...c, destinations: [...c.destinations, { id: crypto.randomUUID(), name: "New destination", provider: "webhook", enabled: true, url: null, headers: null, payloadTemplate: '{\n  "text": "{{function}} failed: {{reason}}",\n  "jobId": "{{tickerId}}"\n}' }] }));
  const addRule = () => change(c => ({ ...c, rules: [...c.rules, { id: crypto.randomUUID(), name: "Job failures", enabled: true, destinationIds: c.destinations[0] ? [c.destinations[0].id] : [], events: ["failed", "timeout_cancelled"], includeFunctions: [], excludeFunctions: [], cooldownSeconds: 60 }] }));

  return <div className="space-y-5">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <PageHeader title="Notifications" description="Choose where alerts go and which functions should send them." />
      <div className="flex items-center gap-2"><span className="text-xs text-muted-foreground">{dirty ? "Unsaved changes" : "Settings saved"}</span><Button size="sm" variant="outline" disabled={busy} onClick={() => dirty ? setConfirm({ type: "reload" }) : void reload()}>Reload</Button><Button size="sm" disabled={!dirty || busy} onClick={() => void save()}>{busy ? "Working…" : "Save changes"}</Button></div>
    </div>
    {!snapshot.persistent && <DataCard className="p-3 text-xs text-amber-700 dark:text-amber-300">The host uses an in-memory store. Notification settings will be lost when it restarts.</DataCard>}
    <fieldset disabled={busy} className="min-w-0 space-y-4">
    <div className="inline-flex gap-1 rounded-lg border border-border bg-surface-1 p-1" aria-label="Notification sections">
      {[['destinations', 'Destinations'], ['rules', 'Rules'], ['delivery', 'Delivery']].map(([key, label]) => <Button key={key} size="sm" variant={tab === key ? "secondary" : "ghost"} aria-pressed={tab === key} onClick={() => setTab(key)}>{label}</Button>)}
    </div>
    {tab === "destinations" && <div className="space-y-4">
      <div className="flex items-center justify-between gap-3"><p className="text-xs text-muted-foreground">Reuse a destination across rules. Saved URLs and headers are never displayed.</p><Button size="sm" variant="outline" disabled={busy || draft.destinations.length >= 32} onClick={addDestination}><Plus className="size-3.5" /> Add destination</Button></div>
      {draft.destinations.length === 0 && <Empty text="Add Slack, Teams, or a custom webhook to receive alerts." />}
      {draft.destinations.map(d => <DataCard key={d.id} className="p-4 space-y-4">
        <div className="flex items-center justify-between gap-3"><div><h2 className="text-sm font-semibold">{d.name}</h2><p className="text-[11px] text-muted-foreground">{providerLabel(d.provider)}{d.endpointHost ? ` · ${d.endpointHost}` : " · Not saved yet"}</p></div><div className="flex items-center gap-4"><Check label="Enabled" checked={d.enabled} change={enabled => destination(d.id, { enabled })} /><Button size="sm" variant="ghost" aria-label={`Remove ${d.name}`} disabled={busy} onClick={() => setConfirm({ type: "delete-destination", id: d.id })}><Trash2 className="size-3.5" /></Button></div></div>
        <div className="grid gap-4 md:grid-cols-2"><Field label="Name"><Input value={d.name} maxLength={80} onChange={e => destination(d.id, { name: e.target.value })} /></Field><Field label="Application"><select className={fieldClass} value={d.provider} onChange={e => { setHeaders(h => ({ ...h, [d.id]: "" })); destination(d.id, { provider: e.target.value, url: null, headers: null, hasUrl: false, hasHeaders: false, endpointHost: null, payloadTemplate: null }); }}>{snapshot.providers.map(p => <option key={p} value={p}>{providerLabel(p)}</option>)}</select></Field></div>
        <Field label="Webhook URL"><Input type="password" autoComplete="new-password" value={d.url ?? ""} placeholder={d.hasUrl ? "Saved securely — enter a URL to replace it" : "https://…"} onChange={e => destination(d.id, { url: e.target.value || null })} /></Field>
        {d.provider === "teams" && <p className="text-xs text-muted-foreground">Use a Teams Workflows webhook that accepts Adaptive Cards.</p>}
        <details className="text-xs"><summary className="cursor-pointer text-muted-foreground">Custom headers {d.hasHeaders ? "· saved securely" : "(optional)"}</summary><textarea aria-label={`Custom headers for ${d.name}`} className={`${fieldClass} mt-2 font-mono`} rows={3} value={headers[d.id] ?? ""} placeholder={'{"Authorization": "Bearer …"}'} onChange={e => { setHeaders(h => ({ ...h, [d.id]: e.target.value })); setDirty(true); }} /><p className="mt-1 text-muted-foreground">Leave blank to keep saved headers. Use {'{}'} to remove them.</p></details>
        {d.provider === "webhook" && <div className="space-y-2"><Field label="JSON payload template"><textarea aria-label="JSON payload template" className={`${fieldClass} font-mono`} rows={6} value={d.payloadTemplate ?? ""} placeholder="Leave empty to send the standard TickerQ event." onChange={e => destination(d.id, { payloadTemplate: e.target.value || null })} /></Field><p className="text-[11px] text-muted-foreground">Use placeholders inside JSON string values. Values are escaped automatically; no scripts run.</p><div className="flex flex-wrap gap-1">{snapshot.templateTokens.map(t => <code key={t} className="rounded bg-surface-2 px-1.5 py-0.5 text-[10px] text-muted-foreground">{`{{${t}}}`}</code>)}</div></div>}
        <div className="flex flex-wrap items-center gap-2"><Button size="sm" variant="outline" disabled={busy} onClick={() => void showPreview(d)}>Preview payload</Button><Button size="sm" variant="outline" disabled={busy || dirty || !d.hasUrl} onClick={() => setConfirm({ type: "test", id: d.id })}><Send className="size-3" /> Send test</Button>{dirty && <span className="text-[11px] text-muted-foreground">Save changes before sending a test.</span>}</div>
        {preview?.id === d.id && <pre className="max-h-72 overflow-auto rounded-md border border-border bg-surface-0 p-3 text-[11px]">{preview.payload}</pre>}
      </DataCard>)}
    </div>}
    {tab === "rules" && <div className="space-y-4">
      <div className="flex items-center justify-between gap-3"><p className="text-xs text-muted-foreground">Rules select events and functions. Overlapping rules send once per destination.</p><Button size="sm" variant="outline" disabled={busy || !draft.destinations.length || draft.rules.length >= 100} onClick={addRule}><Plus className="size-3.5" /> Add rule</Button></div>
      {draft.rules.length === 0 && <Empty text="Add a destination, then create a rule for the jobs you want to monitor." />}
      {draft.rules.map(r => <DataCard key={r.id} className="p-4 space-y-4">
        <div className="flex items-center justify-between gap-3"><h2 className="text-sm font-semibold">{r.name}</h2><div className="flex items-center gap-4"><Check label="Enabled" checked={r.enabled} change={enabled => rule(r.id, { enabled })} /><Button size="sm" variant="ghost" aria-label={`Remove ${r.name}`} disabled={busy} onClick={() => setConfirm({ type: "delete-rule", id: r.id })}><Trash2 className="size-3.5" /></Button></div></div>
        <Field label="Rule name"><Input value={r.name} maxLength={80} onChange={e => rule(r.id, { name: e.target.value })} /></Field>
        <div className="grid gap-4 md:grid-cols-2"><div className="space-y-2"><p className="text-xs font-medium">Send to</p>{draft.destinations.map(d => <Check key={d.id} label={`${d.name}${d.enabled ? "" : " (disabled)"}`} checked={r.destinationIds.includes(d.id)} change={on => rule(r.id, { destinationIds: on ? [...r.destinationIds, d.id] : r.destinationIds.filter(id => id !== d.id) })} />)}</div><div className="space-y-2"><p className="text-xs font-medium">Events</p>{Object.entries(eventLabels).map(([key, label]) => <Check key={key} label={label} checked={r.events.includes(key)} change={on => rule(r.id, { events: on ? [...r.events, key] : r.events.filter(e => e !== key) })} />)}</div></div>
        <div className="grid gap-4 md:grid-cols-2"><Field label="Include functions"><PatternInput value={r.includeFunctions} placeholder="All functions, or Payments.*, ProcessOrders" change={includeFunctions => rule(r.id, { includeFunctions })} /></Field><Field label="Exclude functions"><PatternInput value={r.excludeFunctions} placeholder="Demo.*, TestJob" change={excludeFunctions => rule(r.id, { excludeFunctions })} /></Field></div>
        <Field label="Add a registered function"><select className={fieldClass} value="" onChange={e => { if (e.target.value && !r.includeFunctions.includes(e.target.value)) rule(r.id, { includeFunctions: [...r.includeFunctions, e.target.value] }); }}><option value="">Choose a function…</option>{functions.data?.map(f => <option key={f.functionName} value={f.functionName}>{f.functionName}</option>)}</select></Field>
        <div className="flex flex-wrap items-end gap-4"><Field label="Cooldown (seconds)"><Input type="number" min={0} max={86400} className="w-32" value={r.cooldownSeconds} onChange={e => rule(r.id, { cooldownSeconds: Number(e.target.value) })} /></Field><p className="max-w-xl text-[11px] text-muted-foreground">Exclusions win. Matching is case-sensitive; * matches any text and ? one character. Cooldown suppresses repeated alerts for the same function and event on this node.</p></div>
      </DataCard>)}
    </div>}
    {tab === "delivery" && <DataCard className="overflow-hidden"><div className="border-b border-border p-4"><h2 className="text-sm font-semibold">Recent delivery on this node</h2><p className="mt-1 text-xs text-muted-foreground">{snapshot.suppressed} suppressed · {snapshot.dropped} dropped. History and delivery queue reset when the process restarts. Reload to update.</p></div>{snapshot.recentDeliveries.length === 0 ? <p className="p-5 text-xs text-muted-foreground">No delivery attempts yet.</p> : <div className="divide-y divide-border">{snapshot.recentDeliveries.map((s, i) => <div key={`${s.atUtc}-${i}`} className="flex flex-wrap items-start justify-between gap-2 p-4 text-xs"><div><span className="font-medium">{draft.destinations.find(d => d.id === s.destinationId)?.name ?? "Notifications"}</span><p className="mt-1 text-muted-foreground">{s.message}</p></div><div className="text-right"><span className={s.outcome === "Sent" ? "text-status-healthy" : "text-status-error"}>{s.outcome}</span><p className="mt-1 text-[10px] text-muted-foreground">{new Date(s.atUtc).toLocaleString()}</p></div></div>)}</div>}</DataCard>}
    </fieldset>
    <ConfirmDialog open={confirm !== null} onOpenChange={open => { if (!open) setConfirm(null); }} title={confirm?.type === "test" ? "Send test notification?" : confirm?.type === "reload" ? "Discard unsaved changes?" : "Remove this item?"} description={confirm?.type === "test" ? "Sends a sample failure message to the saved destination, even if it is disabled. No job will run." : confirm?.type === "delete-destination" ? "Removes this destination from every rule. Select another destination for any empty rules, or remove those rules, then save." : confirm?.type === "reload" ? "Reloads the latest settings from the server." : "The rule will be removed when you save changes."} confirmLabel={confirm?.type === "test" ? "Send test" : confirm?.type === "reload" ? "Discard and reload" : "Remove"} confirmVariant={confirm?.type === "test" ? "default" : "destructive"} isPending={busy} onConfirm={() => void confirmed()} />
  </div>;
}
function Field({ label, children }: { label: string; children: ReactNode }) { return <label className="block space-y-1.5"><span className="text-xs font-medium">{label}</span>{children}</label>; }
function Check({ label, checked, change }: { label: string; checked: boolean; change: (value: boolean) => void }) { return <label className="flex items-center gap-2 text-xs"><Checkbox checked={checked} onCheckedChange={value => change(value === true)} />{label}</label>; }
function Empty({ text }: { text: string }) { return <DataCard className="flex items-center gap-3 p-6 text-sm text-muted-foreground"><Bell className="size-5 shrink-0" />{text}</DataCard>; }
function PatternInput({ value, change, placeholder }: { value: string[]; change: (value: string[]) => void; placeholder: string }) {
  const [text, setText] = useState(value.join(", "));
  const [previous, setPrevious] = useState(value);
  if (previous !== value) { setPrevious(value); if (splitPatterns(text).join("\n") !== value.join("\n")) setText(value.join(", ")); }
  return <Input value={text} placeholder={placeholder} onChange={e => { setText(e.target.value); change(splitPatterns(e.target.value)); }} />;
}
