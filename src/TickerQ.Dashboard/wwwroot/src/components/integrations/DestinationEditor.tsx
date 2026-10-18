import { useRef, useState, type FormEvent } from "react";
import { ChevronDown, Code2, Eye, LockKeyhole, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { dashboardApi } from "@/services/dashboard-api";
import type { IntegrationDestination } from "@/services/integration-types";
import { EditorShell } from "./EditorShell";
import { Choice, Field, ProviderIcon, Section, fieldClass } from "./Fields";
import {
  errorText,
  parseHeaders,
  providerName,
  sampleTemplate,
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
  const [draft, setDraft] = useState(() => structuredClone(initial));
  const [headers, setHeaders] = useState("");
  const [clearHeaders, setClearHeaders] = useState(false);
  const [customPayload, setCustomPayload] = useState(!!initial.payloadTemplate);
  const [busy, setBusy] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const payloadInput = useRef<HTMLTextAreaElement>(null);
  const isNew = !initial.hasUrl;
  const dirty =
    isNew ||
    JSON.stringify(draft) !== JSON.stringify(initial) ||
    headers !== "" ||
    clearHeaders ||
    customPayload !== !!initial.payloadTemplate;
  function patch(value: Partial<IntegrationDestination>) {
    setDraft((d) => ({ ...d, ...value }));
    setPreview(null);
    setError(null);
  }
  function insertToken(token: string) {
    const input = payloadInput.current;
    const value = draft.payloadTemplate ?? "";
    const start = input?.selectionStart ?? value.length;
    const end = input?.selectionEnd ?? start;
    const insertion = `{{${token}}}`;
    patch({
      payloadTemplate: value.slice(0, start) + insertion + value.slice(end),
    });
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
        payloadTemplate: customPayload ? draft.payloadTemplate : null,
      };
      if (customPayload && !value.payloadTemplate?.trim())
        throw new Error(
          "Enter a JSON template, or select the default payload.",
        );
      setBusy(true);
      await onSave(value);
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  async function showPreview() {
    setPreviewing(true);
    setError(null);
    try {
      if (customPayload && !draft.payloadTemplate?.trim())
        throw new Error("Enter a JSON template, or select the default payload.");
      const result = await dashboardApi.previewIntegration(
        draft.provider,
        customPayload ? draft.payloadTemplate : null,
      );
      setPreview(JSON.stringify(JSON.parse(result.payload), null, 2));
    } catch (e) {
      setError(errorText(e));
    } finally {
      setPreviewing(false);
    }
  }
  return (
    <EditorShell
      title={
        isNew ? `Connect ${providerName(draft.provider)}` : `Edit ${draft.name}`
      }
      description="Choose where your job alerts are delivered."
      icon={<ProviderIcon provider={draft.provider} />}
      dirty={dirty}
      busy={busy || previewing}
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
            Deliver alerts when an enabled rule matches.
          </span>
        </span>
        <Switch
          checked={draft.enabled}
          onCheckedChange={(enabled) => patch({ enabled })}
        />
      </label>
      <Section
        title="Payload"
        description={draft.provider === "slack"
          ? "Customize the message text and Slack Block Kit layout."
          : draft.provider === "teams"
            ? "Customize the Teams message and Adaptive Card layout."
            : "Use the default event or shape it for your application."}
      >
        <div
          className="flex rounded-lg border border-border bg-surface-1 p-1"
          role="group"
          aria-label="Payload format"
        >
          <Button
            type="button"
            size="sm"
            className="flex-1"
            variant={!customPayload ? "secondary" : "ghost"}
            aria-pressed={!customPayload}
            onClick={() => {
              setCustomPayload(false);
              setPreview(null);
            }}
          >
            Default
          </Button>
          <Button
            type="button"
            size="sm"
            className="flex-1"
            variant={customPayload ? "secondary" : "ghost"}
            aria-pressed={customPayload}
            onClick={() => {
              setCustomPayload(true);
              if (!draft.payloadTemplate)
                patch({ payloadTemplate: sampleTemplate(draft.provider) });
              setPreview(null);
            }}
          >
            <Code2 className="size-3.5" /> Custom JSON
          </Button>
        </div>
        {customPayload ? (
          <div className="space-y-3">
            <Field
              label="JSON payload template"
              hint="Placeholders go inside JSON string values. Values are escaped automatically."
            >
              <textarea
                ref={payloadInput}
                className={`${fieldClass} min-h-44 font-mono leading-relaxed`}
                rows={7}
                maxLength={16000}
                spellCheck={false}
                value={draft.payloadTemplate ?? ""}
                onChange={(e) => patch({ payloadTemplate: e.target.value })}
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
        ) : (
          <p className="text-xs leading-relaxed text-muted-foreground">
            Uses the built-in {providerName(draft.provider)} payload. Preview
            the exact structure below, or select Custom JSON to edit it.
          </p>
        )}
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
      <Section
        title="Preview message"
        description="Uses sample values. Nothing is sent to the integration."
      >
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={busy || previewing}
          onClick={() => void showPreview()}
        >
          <Eye className="size-3.5" />
          {previewing ? "Preparing…" : "Preview payload"}
        </Button>
        {preview && (
          <pre
            aria-label="Payload preview"
            className="max-h-72 overflow-auto rounded-lg border border-border bg-surface-0 p-3 font-mono text-[11px] leading-relaxed"
          >
            {preview}
          </pre>
        )}
      </Section>
      {isNew && (
        <p className="flex items-start gap-2 rounded-lg bg-primary/5 p-3 text-xs leading-relaxed text-muted-foreground">
          <Plus className="mt-0.5 size-3.5 shrink-0 text-primary" />
          After saving, add an alert rule to choose which events and functions
          send here.
        </p>
      )}
    </EditorShell>
  );
}
