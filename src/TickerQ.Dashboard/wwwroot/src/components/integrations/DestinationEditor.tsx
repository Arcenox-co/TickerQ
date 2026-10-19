import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  ChevronDown,
  Code2,
  Eye,
  LockKeyhole,
  Plus,
  CircleCheck,
  BellRing,
  TriangleAlert,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { dashboardApi } from "@/services/dashboard-api";
import type {
  IntegrationDestination,
  IntegrationMessageCategory,
} from "@/services/integration-types";
import { MessagePreview } from "./MessagePreview";
import { EditorShell } from "./EditorShell";
import { Choice, Field, ProviderIcon, Section, fieldClass } from "./Fields";
import {
  errorText,
  parseHeaders,
  providerName,
  editableDestination,
} from "./configuration";

export function DestinationEditor({
  initial,
  tokens,
  onClose,
  onSave,
}: {
  initial: IntegrationDestination;
  tokens: string[];
  onClose: () => void;
  onSave: (value: IntegrationDestination) => Promise<void>;
}) {
  const [baseline] = useState(() => editableDestination(initial));
  const [draft, setDraft] = useState(() => editableDestination(initial));
  const [headers, setHeaders] = useState("");
  const [clearHeaders, setClearHeaders] = useState(false);
  const [category, setCategory] =
    useState<IntegrationMessageCategory>("jobAlert");
  const [sampleKind, setSampleKind] = useState("long_running");
  const [showJson, setShowJson] = useState(false);
  const [defaultTemplate, setDefaultTemplate] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const template = draft.messageTemplates?.[category] ?? null;
  const customPayload = template !== null;
  const [busy, setBusy] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const payloadInput = useRef<HTMLTextAreaElement>(null);
  const isNew = !initial.hasUrl;
  const dirty =
    isNew ||
    JSON.stringify(draft) !== JSON.stringify(baseline) ||
    headers !== "" ||
    clearHeaders;
  function patch(value: Partial<IntegrationDestination>) {
    setDraft((d) => ({ ...d, ...value }));
    setError(null);
  }
  function setTemplate(value: string | null) {
    patch({
      messageTemplates: { ...draft.messageTemplates!, [category]: value },
    });
  }
  useEffect(() => {
    let active = true;
    setPreview(null);
    setPreviewError(null);
    setDefaultTemplate(null);
    setPreviewing(true);
    const timer = setTimeout(
      async () => {
        try {
          if (template !== null && !template.trim())
            throw new Error(
              "Enter a JSON template or use the default message.",
            );
          const kind = {
            jobAlert: "job_alert",
            monitoringAlert: "monitoring_alert",
            recovery: "recovery",
          }[category];
          const result = await dashboardApi.previewIntegration(
            draft.provider,
            template,
            kind,
            sampleKind,
          );
          if (active) {
            setPreview(JSON.stringify(JSON.parse(result.payload), null, 2));
            setDefaultTemplate(
              result.defaultTemplate
                ? JSON.stringify(JSON.parse(result.defaultTemplate), null, 2)
                : null,
            );
          }
        } catch (e) {
          if (active) setPreviewError(errorText(e));
        } finally {
          if (active) setPreviewing(false);
        }
      },
      template === null ? 0 : 400,
    );
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [draft.provider, category, sampleKind, template]);
  function insertToken(token: string) {
    const input = payloadInput.current;
    const value = template ?? "";
    const start = input?.selectionStart ?? value.length;
    const end = input?.selectionEnd ?? start;
    const insertion = `{{${token}}}`;
    setTemplate(value.slice(0, start) + insertion + value.slice(end));
    requestAnimationFrame(() => {
      input?.focus();
      input?.setSelectionRange(
        start + insertion.length,
        start + insertion.length,
      );
    });
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    try {
      if (!draft.name.trim()) throw new Error("Give this integration a name.");
      if (!draft.url?.trim() && !draft.hasUrl)
        throw new Error("Enter the HTTPS webhook URL for this integration.");
      const value = {
        ...draft,
        name: draft.name.trim(),
        url: draft.url?.trim() || null,
        headers: clearHeaders ? {} : parseHeaders(headers),
        payloadTemplate: null,
      };
      for (const [key, content] of Object.entries(value.messageTemplates!)) {
        if (content !== null && !content.trim())
          throw new Error(
            `${key}: enter a JSON template or use the default message.`,
          );
      }
      setBusy(true);
      await onSave(value);
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <EditorShell
      title={
        isNew ? `Connect ${providerName(draft.provider)}` : `Edit ${draft.name}`
      }
      description="Choose where alerts arrive and how each message looks."
      icon={<ProviderIcon provider={draft.provider} />}
      dirty={dirty}
      busy={busy}
      error={error}
      saveLabel={isNew ? "Save integration" : "Save changes"}
      onClose={onClose}
      onSubmit={(event) => void submit(event)}
    >
      <Section title="Connection">
        <Field
          label="Integration name"
          hint="Use a name your team will recognize."
        >
          <Input
            autoFocus
            required
            maxLength={80}
            value={draft.name}
            placeholder={
              draft.provider === "slack"
                ? "e.g. Engineering · #job-alerts"
                : "e.g. Production alerts"
            }
            onChange={(e) => patch({ name: e.target.value })}
          />
        </Field>
        <Field
          label="Webhook URL"
          hint={
            draft.provider === "teams"
              ? "Use a Teams Workflows URL that accepts Adaptive Cards."
              : draft.provider === "slack"
                ? "Paste the incoming webhook URL for your Slack channel."
                : "Alerts are sent as an HTTPS POST with a JSON body."
          }
        >
          <Input
            type="password"
            autoComplete="new-password"
            spellCheck={false}
            required={!draft.hasUrl}
            value={draft.url ?? ""}
            placeholder={
              draft.hasUrl
                ? "Saved securely · enter a URL to replace it"
                : "https://…"
            }
            onChange={(e) => patch({ url: e.target.value || null })}
          />
        </Field>
        <p className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
          <LockKeyhole className="size-3.5 shrink-0" />
          Webhook URLs and header values are stored securely and never shown.
        </p>
      </Section>
      <label className="flex items-center justify-between gap-4 rounded-lg border border-border bg-surface-1 p-3">
        <span>
          <span className="block text-xs font-medium">Enable integration</span>
          <span className="mt-1 block text-[11px] text-muted-foreground">
            Deliver alerts from enabled rules and monitors.
          </span>
        </span>
        <Switch
          checked={draft.enabled}
          onCheckedChange={(enabled) => patch({ enabled })}
        />
      </label>
      <Section
        title="Message templates"
        description="Set a different message for each event. Defaults are ready to use."
      >
        <div
          className="grid grid-cols-3 gap-1 rounded-lg border border-border bg-surface-0 p-1"
          role="group"
          aria-label="Message category"
        >
          {(
            [
              ["jobAlert", "Job alert", TriangleAlert],
              ["monitoringAlert", "Monitor alert", BellRing],
              ["recovery", "Recovery", CircleCheck],
            ] as const
          ).map(([key, label, Icon]) => (
            <button
              key={key}
              type="button"
              aria-pressed={category === key}
              onClick={() => setCategory(key)}
              className={`flex min-w-0 flex-col items-center gap-1.5 rounded-md px-2 py-2.5 text-[11px] font-medium transition-colors focus-visible:outline-2 focus-visible:outline-primary ${category === key ? "bg-surface-2 text-foreground shadow-sm" : "text-muted-foreground hover:bg-surface-1 hover:text-foreground"}`}
            >
              <Icon
                className={`size-4 ${category === key ? (key === "recovery" ? "text-status-healthy" : "text-primary") : ""}`}
              />
              {label}
              <span className="text-[10px] font-normal text-muted-foreground">
                {draft.messageTemplates?.[key] != null ? "Custom" : "Default"}
              </span>
            </button>
          ))}
        </div>
        <p className="text-xs leading-relaxed text-muted-foreground">
          {category === "jobAlert"
            ? "Sent for failures, timeouts, abandoned jobs, and request changes."
            : category === "monitoringAlert"
              ? "Sent when a deadline, duration, or failure threshold is breached."
              : "Sent after a monitor confirms recovery, when recovery messages are enabled."}
        </p>
        <div className="flex items-center justify-between gap-3 rounded-lg border border-border bg-surface-1 px-3 py-2">
          <span className="text-xs font-medium">
            {customPayload ? "Custom JSON template" : "Default message"}
          </span>
          {customPayload ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setTemplate(null)}
            >
              Use default
            </Button>
          ) : (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={!defaultTemplate || previewing}
              onClick={() => setTemplate(defaultTemplate)}
            >
              <Code2 className="size-3.5" /> Customize
            </Button>
          )}
        </div>
        {customPayload && (
          <div className="space-y-3">
            <Field
              label="JSON payload template"
              hint="Edit this category only. Placeholders go inside JSON string values."
            >
              <textarea
                ref={payloadInput}
                className={`${fieldClass} min-h-52 font-mono leading-relaxed`}
                rows={10}
                maxLength={16000}
                spellCheck={false}
                value={template ?? ""}
                onChange={(e) => setTemplate(e.target.value)}
              />
            </Field>
            <details className="group rounded-lg border border-border">
              <summary className="flex cursor-pointer list-none items-center justify-between px-3 py-2 text-xs text-muted-foreground">
                Insert a placeholder
                <ChevronDown className="size-3.5 transition-transform group-open:rotate-180" />
              </summary>
              <div className="flex flex-wrap gap-1.5 border-t border-border p-3">
                {tokens.map((token) => (
                  <button
                    key={token}
                    type="button"
                    className="rounded border border-border bg-surface-1 px-2 py-1 font-mono text-[11px] text-muted-foreground hover:border-primary/40 hover:text-primary"
                    onClick={() => insertToken(token)}
                  >{`{{${token}}}`}</button>
                ))}
              </div>
            </details>
          </div>
        )}
        {category !== "jobAlert" && (
          <Field
            label="Preview scenario"
            hint="Sample data only. This does not change the monitor or send a message."
          >
            <select
              className={fieldClass}
              value={sampleKind}
              onChange={(e) => setSampleKind(e.target.value)}
            >
              <option value="long_running">Long-running execution</option>
              <option value="consecutive_failures">Repeated failures</option>
              <option value="deadline">Daily deadline</option>
            </select>
          </Field>
        )}
        <div
          className="overflow-hidden rounded-xl border border-border bg-surface-0"
          aria-live="polite"
        >
          <div className="flex items-center justify-between border-b border-border bg-surface-1 px-3 py-2">
            <span className="flex items-center gap-2 text-xs font-medium">
              <Eye className="size-3.5 text-muted-foreground" /> Message preview
            </span>
            {(draft.provider === "slack" || draft.provider === "teams") && (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                aria-pressed={showJson}
                onClick={() => setShowJson((value) => !value)}
              >
                {showJson ? "Show layout" : "View JSON"}
              </Button>
            )}
          </div>
          {previewing ? (
            <p className="p-4 text-xs text-muted-foreground">
              Updating preview…
            </p>
          ) : previewError ? (
            <p role="alert" className="p-4 text-xs text-status-error">
              {previewError}
            </p>
          ) : (
            preview &&
            (showJson ? (
              <pre
                aria-label="Payload preview"
                className="max-h-80 overflow-auto p-4 font-mono text-[11px] leading-relaxed"
              >
                {preview}
              </pre>
            ) : (
              <MessagePreview provider={draft.provider} payload={preview} />
            ))
          )}
          <p className="border-t border-border px-3 py-2 text-[10px] leading-relaxed text-muted-foreground">
            Sample data · Nothing is sent. Layout previews show supported text
            elements; the receiving app controls final rendering.
          </p>
        </div>
      </Section>
      <details className="group rounded-lg border border-border">
        <summary className="flex cursor-pointer list-none items-center justify-between p-3 text-xs font-medium">
          Advanced settings
          <ChevronDown className="size-3.5 text-muted-foreground transition-transform group-open:rotate-180" />
        </summary>
        <div className="space-y-3 border-t border-border p-3">
          <Field
            label="Custom headers"
            hint={
              draft.hasHeaders
                ? "Saved headers are hidden. Leave blank to keep them, or enter a complete replacement."
                : "Optional JSON object, for endpoints that require authentication."
            }
          >
            <textarea
              className={`${fieldClass} font-mono`}
              rows={3}
              spellCheck={false}
              autoComplete="off"
              disabled={clearHeaders}
              value={headers}
              placeholder={'{\n  "Authorization": "Bearer …"\n}'}
              onChange={(e) => {
                setHeaders(e.target.value);
                setError(null);
              }}
            />
          </Field>
          {draft.hasHeaders && (
            <Choice
              label="Remove saved headers"
              description="The next save will clear all custom headers for this integration."
              checked={clearHeaders}
              onChange={setClearHeaders}
            />
          )}
        </div>
      </details>
      {isNew && (
        <p className="flex items-start gap-2 rounded-lg bg-primary/5 p-3 text-xs leading-relaxed text-muted-foreground">
          <Plus className="mt-0.5 size-3.5 shrink-0 text-primary" />
          After saving, add an alert rule or monitor to choose what sends here.
        </p>
      )}
    </EditorShell>
  );
}
