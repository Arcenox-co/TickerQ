import { useState, type FormEvent } from "react";
import { ChevronDown, ListFilter, Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { useAllFunctions } from "@/services/hooks";
import type {
  IntegrationDestination,
  IntegrationRule,
} from "@/services/integration-types";
import { EditorShell } from "./EditorShell";
import { Choice, Field, Section, fieldClass } from "./Fields";
import {
  errorText,
  eventOptions,
  providerName,
  splitPatterns,
} from "./configuration";

export function RuleEditor({
  initial,
  isNew,
  destinations,
  onClose,
  onSave,
}: {
  initial: IntegrationRule;
  isNew: boolean;
  destinations: IntegrationDestination[];
  onClose: () => void;
  onSave: (value: IntegrationRule) => Promise<void>;
}) {
  const [draft, setDraft] = useState(() => structuredClone(initial));
  const [includeText, setIncludeText] = useState(
    initial.includeFunctions.join(", "),
  );
  const [excludeText, setExcludeText] = useState(
    initial.excludeFunctions.join(", "),
  );
  const [allFunctions, setAllFunctions] = useState(
    initial.includeFunctions.length === 0,
  );
  const [search, setSearch] = useState("");
  const [cooldown, setCooldown] = useState(String(initial.cooldownSeconds));
  const [advancedOpen, setAdvancedOpen] = useState(
    initial.excludeFunctions.length > 0,
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const functions = useAllFunctions();
  const include = splitPatterns(includeText);
  const dirty =
    isNew ||
    JSON.stringify(draft) !== JSON.stringify(initial) ||
    includeText !== initial.includeFunctions.join(", ") ||
    excludeText !== initial.excludeFunctions.join(", ") ||
    allFunctions !== (initial.includeFunctions.length === 0) ||
    cooldown !== String(initial.cooldownSeconds);
  const matchingFunctions = (functions.data ?? []).filter((f) =>
    f.functionName.toLowerCase().includes(search.toLowerCase()),
  );
  function patch(value: Partial<IntegrationRule>) {
    setDraft((d) => ({ ...d, ...value }));
    setError(null);
  }
  function selectFunction(name: string, checked: boolean) {
    setIncludeText(
      (checked
        ? [...include.filter((p) => p !== name), name]
        : include.filter((p) => p !== name)
      ).join(", "),
    );
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    try {
      if (!draft.name.trim()) throw new Error("Give this rule a name.");
      if (!draft.destinationIds.length)
        throw new Error("Choose at least one integration to receive alerts.");
      if (!draft.events.length) throw new Error("Choose at least one event.");
      if (!allFunctions && !include.length)
        throw new Error("Choose a function or enter a name pattern.");
      const seconds = Number(cooldown);
      if (
        !cooldown.trim() ||
        !Number.isInteger(seconds) ||
        seconds < 0 ||
        seconds > 86400
      )
        throw new Error("Enter a cooldown from 0 to 86,400 seconds.");
      setBusy(true);
      await onSave({
        ...draft,
        name: draft.name.trim(),
        includeFunctions: allFunctions ? [] : include,
        excludeFunctions: splitPatterns(excludeText),
        cooldownSeconds: seconds,
      });
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <EditorShell
      title={isNew ? "Create alert rule" : "Edit alert rule"}
      description="Choose when an alert is sent and who receives it."
      icon={
        <span className="flex size-11 shrink-0 items-center justify-center rounded-xl border border-primary/20 bg-primary/10 text-primary">
          <ListFilter className="size-5" />
        </span>
      }
      dirty={dirty}
      busy={busy}
      error={error}
      saveLabel={isNew ? "Create rule" : "Save changes"}
      onClose={onClose}
      onSubmit={(event) => void submit(event)}
    >
      <Field label="Rule name">
        <Input
          autoFocus
          required
          maxLength={80}
          placeholder="e.g. Production job failures"
          value={draft.name}
          onChange={(e) => patch({ name: e.target.value })}
        />
      </Field>
      <Section
        title="Send to"
        description="An event is sent once to each matching integration."
      >
        <div className="grid gap-2 sm:grid-cols-2">
          {destinations.map((d) => (
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
          destinations
            .filter((d) => draft.destinationIds.includes(d.id))
            .every((d) => !d.enabled) && (
            <p className="text-xs text-amber-600 dark:text-amber-300">
              All selected integrations are paused. Enable one to deliver
              alerts.
            </p>
          )}
      </Section>
      <Section title="When this happens">
        <div className="grid gap-2">
          {eventOptions.map((e) => (
            <Choice
              key={e.value}
              label={e.label}
              description={e.description}
              checked={draft.events.includes(e.value)}
              onChange={(checked) =>
                patch({
                  events: checked
                    ? [...draft.events, e.value]
                    : draft.events.filter((value) => value !== e.value),
                })
              }
            />
          ))}
        </div>
      </Section>
      <Section title="For these functions">
        <div
          role="group"
          aria-label="Function scope"
          className="flex rounded-lg border border-border bg-surface-1 p-1"
        >
          <Button
            type="button"
            size="sm"
            className="flex-1"
            aria-pressed={allFunctions}
            variant={allFunctions ? "secondary" : "ghost"}
            onClick={() => setAllFunctions(true)}
          >
            All functions
          </Button>
          <Button
            type="button"
            size="sm"
            className="flex-1"
            aria-pressed={!allFunctions}
            variant={!allFunctions ? "secondary" : "ghost"}
            onClick={() => setAllFunctions(false)}
          >
            Selected functions
          </Button>
        </div>
        {allFunctions ? (
          <p className="text-xs text-muted-foreground">
            Includes current and future functions, except any exclusions below.
          </p>
        ) : (
          <div className="space-y-3">
            <div className="relative">
              <Search className="absolute top-2.5 left-3 size-3.5 text-muted-foreground" />
              <Input
                aria-label="Search registered functions"
                className="pl-9"
                placeholder="Search registered functions…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <div className="max-h-48 space-y-1.5 overflow-y-auto rounded-lg border border-border p-2">
              {functions.isPending ? (
                <p className="p-2 text-xs text-muted-foreground">
                  Loading functions…
                </p>
              ) : functions.isError ? (
                <p className="p-2 text-xs text-status-error">
                  Could not load functions. Enter their names below.
                </p>
              ) : matchingFunctions.length ? (
                matchingFunctions
                  .slice(0, 40)
                  .map((f) => (
                    <Choice
                      key={f.functionName}
                      label={f.functionName}
                      checked={include.includes(f.functionName)}
                      onChange={(checked) =>
                        selectFunction(f.functionName, checked)
                      }
                    />
                  ))
              ) : (
                <p className="p-2 text-xs text-muted-foreground">
                  No matching functions. You can enter a name or pattern below.
                </p>
              )}
              {matchingFunctions.length > 40 && (
                <p className="p-2 text-[11px] text-muted-foreground">
                  Showing 40 matches. Refine your search to find more.
                </p>
              )}
            </div>
            <Field
              label="Selected names or patterns"
              hint="Separate with commas. Payments.* matches a prefix; ProcessOrder? matches one final character."
            >
              <textarea
                rows={2}
                className={fieldClass}
                value={includeText}
                placeholder="ProcessOrders, Payments.*"
                onChange={(e) => setIncludeText(e.target.value)}
              />
            </Field>
          </div>
        )}
      </Section>
      <details
        className="group rounded-lg border border-border"
        open={advancedOpen}
        onToggle={(event) => setAdvancedOpen(event.currentTarget.open)}
      >
        <summary className="flex cursor-pointer list-none items-center justify-between p-3 text-xs font-medium">
          Exclusions & cooldown
          <ChevronDown className="size-3.5 text-muted-foreground transition-transform group-open:rotate-180" />
        </summary>
        <div className="space-y-4 border-t border-border p-3">
          <Field
            label="Exclude functions"
            hint="Exclusions always win. Names and patterns are case-sensitive."
          >
            <Input
              placeholder="Demo.*, TestJob"
              value={excludeText}
              onChange={(e) => setExcludeText(e.target.value)}
            />
          </Field>
          <Field
            label="Cooldown (seconds)"
            hint="Wait this long before another alert for the same function and event on this node. Set 0 to send every event."
          >
            <Input
              className="max-w-36"
              type="number"
              min={0}
              max={86400}
              step={1}
              required
              value={cooldown}
              onChange={(e) => setCooldown(e.target.value)}
            />
          </Field>
        </div>
      </details>
      <label className="flex items-center justify-between gap-4 rounded-lg border border-border bg-surface-1 p-3">
        <span>
          <span className="block text-xs font-medium">Enable rule</span>
          <span className="mt-1 block text-[11px] text-muted-foreground">
            Start matching events as soon as you save.
          </span>
        </span>
        <Switch
          checked={draft.enabled}
          onCheckedChange={(enabled) => patch({ enabled })}
        />
      </label>
    </EditorShell>
  );
}
