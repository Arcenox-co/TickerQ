import { Code2, Settings2 } from "lucide-react";

import type { Nouns } from "@/lib/promise-list";
import { durationLabel } from "@/lib/promise-settings";
import { everyLabel } from "@/lib/relative-time";
import type { PromiseSnapshot } from "@/services/promise-types";

/**
 * The promise as the developer declared it, one builder call per line in plain words, and the application-wide
 * options it is checked with. Where the dashboard has overridden the code, the line says so.
 */
export function PromiseRule({ promise, words }: { promise: PromiseSnapshot; words: Nouns }) {
  const { rule, settings, autoFix } = promise;
  const withinChanged = settings.withinSeconds != null && settings.withinSeconds !== rule.withinSeconds;
  const autoFixChanged = autoFix.enabled !== (rule.autoFixMax != null);
  const checks = rule.checks;
  const onMapTicker = rule.declaredOn === "MapTicker";
  const registration = rule.functionClass
    ? `${onMapTicker ? "MapTicker" : "For"}<${rule.functionClass}, ${rule.requestType}>()`
    : onMapTicker ? `MapTicker("${promise.function}", …)` : "For(TickerFunctions.…)";

  return <div className="space-y-4">
    <Section icon={Code2} title="Declared in code"
      hint={`Set up with ${onMapTicker ? "services.MapTicker(…).Promise(…)" : "options.ConfigurePromises(…)"}. Change it in code and redeploy.`}>
      <Line label="Rows" call={onMapTicker ? ".Promise(rows, …)" : "For(…, rows, …)"}>
        {rule.rowType} rows{rule.rowsFrom && <> from <Mono>{rule.rowsFrom}</Mono></>}
        <Note>The query is code, so it isn't shown here.</Note>
      </Line>
      <Line label="Job" call={registration}>
        <Mono>{promise.function}</Mono>, run with a <Mono>{rule.requestType}</Mono>
      </Line>
      <Line label="Matched by" call=".Job(…, matchBy)">
        {rule.matchBy.map((part, i) => <span key={part}>{i > 0 && " + "}<Mono>{part}</Mono></span>)}
        <Note>A job belongs to a {words.one} when its request has the same {rule.matchBy.length > 1 ? "values" : "value"}.</Note>
      </Line>
      <Line label="Time limit" call=".Within(…)">
        {durationLabel(rule.withinSeconds)} after <Mono>{rule.after ?? "its time"}</Mono>
        {withinChanged && <Override>Changed in the dashboard to {durationLabel(settings.withinSeconds!)}.</Override>}
      </Line>
      <Line label="Done when" call=".DoneWhen(…)">
        {rule.doneWhen
          ? <>“{rule.doneWhen}”</>
          : <span className="text-muted-foreground">Not set — a {words.one} is done once one of its jobs succeeds.</span>}
      </Line>
      <Line label="Row name" call=".Label(…)">
        {rule.hasLabel ? `Set in code` : <span className="text-muted-foreground">Not set — {words.many} are named by their match value.</span>}
      </Line>
      <Line label="Promise name" call=".Named(…)">
        {rule.namedInCode ? <>“{rule.namedInCode}”</> : <span className="text-muted-foreground">Not set — uses “{promise.name}”.</span>}
      </Line>
      <Line label="Automatic fixing" call=".AutoFix(…)">
        {rule.autoFixMax != null
          ? <>On — schedules up to {rule.autoFixMax} never-scheduled {words.many} per check</>
          : <span className="text-muted-foreground">Not set — a person fixes {words.many}.</span>}
        {autoFixChanged && <Override>Turned {autoFix.enabled ? "on" : "off"} in the dashboard.</Override>}
      </Line>
      <Line label="Repeats" call=".AllowRepeats()">
        {rule.allowRepeats
          ? <>Allowed — a {words.one} may have more than one successful job.</>
          : <span className="text-muted-foreground">Not set — a {words.one} whose job runs twice needs review.</span>}
      </Line>
    </Section>

    <Section icon={Settings2} title="Checked with" hint="Options for every promise, set with ConfigurePromises.">
      <Line label="How often" call="CheckInterval">{everyLabel(checks.checkIntervalSeconds).replace(/^every/, "Every")}</Line>
      <Line label="Never ran after" call="NeverRanAfter">
        {durationLabel(checks.neverRanAfterSeconds)}
        <Note>A waiting job this late past its due time counts as never ran.</Note>
      </Line>
      <Line label="Looks back" call={checks.lookBackFromRetention ? "Job retention" : "LookBack"}>
        {formatDays(checks.lookBackDays)}
        <Note>{checks.lookBackFromRetention
          ? "The shortest retention window, so no row is reported because its job was deleted."
          : `${words.many[0].toUpperCase()}${words.many.slice(1)} older than this aren't checked.`}</Note>
      </Line>
      <Line label="Per check" call="MaxRowsPerCheck · MaxJobsPerCheck">
        Up to {checks.maxRowsPerCheck.toLocaleString()} {words.many} and {checks.maxJobsPerCheck.toLocaleString()} jobs
      </Line>
    </Section>
  </div>;
}

function formatDays(days: number): string {
  if (days >= 1) return Number.isInteger(days) ? `${days} day${days === 1 ? "" : "s"}` : `${days.toFixed(1)} days`;
  return durationLabel(Math.round(days * 86_400));
}

function Section({ icon: Icon, title, hint, children }: {
  icon: typeof Code2;
  title: string;
  hint: string;
  children: React.ReactNode;
}) {
  return <section aria-label={title} className="overflow-hidden rounded-lg border">
    <header className="flex items-start gap-2.5 border-b bg-muted/30 px-4 py-2.5">
      <Icon className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" aria-hidden />
      <div>
        <h3 className="text-sm font-medium">{title}</h3>
        <p className="text-[11px] text-muted-foreground">{hint}</p>
      </div>
    </header>
    <dl className="divide-y">{children}</dl>
  </section>;
}

function Line({ label, call, children }: { label: string; call: string; children: React.ReactNode }) {
  return <div className="grid gap-1 px-4 py-2.5 text-xs sm:grid-cols-[9rem_minmax(0,1fr)_auto] sm:items-baseline sm:gap-3">
    <dt className="text-muted-foreground">{label}</dt>
    <dd className="min-w-0">{children}</dd>
    <dd className="hidden font-mono text-[10px] text-muted-foreground/70 sm:block">{call}</dd>
  </div>;
}

function Mono({ children }: { children: React.ReactNode }) {
  return <code className="rounded bg-muted px-1 py-px font-mono text-[11px]">{children}</code>;
}

function Note({ children }: { children: React.ReactNode }) {
  return <span className="mt-0.5 block text-[11px] text-muted-foreground">{children}</span>;
}

function Override({ children }: { children: React.ReactNode }) {
  return <span className="mt-0.5 block text-[11px] text-amber-700 dark:text-amber-300">{children}</span>;
}
