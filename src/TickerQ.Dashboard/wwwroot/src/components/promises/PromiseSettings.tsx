import { useId } from "react";

import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import type { PromiseActivity, PromiseSnapshot } from "@/services/promise-types";
import { autoFixFollowsCode, durationLabel, effectiveWithin, withinChange, withinChoices } from "@/lib/promise-settings";
import type { Nouns } from "@/lib/promise-list";
import { cn } from "@/lib/utils";
import { RelativeTime } from "./RelativeTime";
import type { PromiseActions } from "./use-promise-actions";

function Row({ title, hint, children }: { title: React.ReactNode; hint?: React.ReactNode; children?: React.ReactNode }) {
  return <div className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-start sm:justify-between">
    <div className="min-w-0 space-y-0.5">
      <div className="text-xs font-medium">{title}</div>
      {hint && <div className="text-[11px] text-muted-foreground">{hint}</div>}
    </div>
    {children}
  </div>;
}

/** Enabled, time limit and automatic fixing. Without manage rights the values are shown, not the controls. */
export function PromiseSettings({ promise, words, actions }: { promise: PromiseSnapshot; words: Nouns; actions: PromiseActions }) {
  const ids = { pause: useId(), within: useId(), autoFix: useId() };
  const { settings, autoFix, canManage } = promise;
  const saving = actions.settings.isPending;
  const save = actions.settings.mutate;
  const within = effectiveWithin(settings);
  const codeWithin = durationLabel(settings.withinSecondsFromCode);
  const followsCode = autoFixFollowsCode(autoFix);

  return <section aria-label="Settings" className="rounded-lg border">
    <div className="divide-y">
      <Row title={<label htmlFor={ids.pause}>Enabled</label>}
        hint="When disabled, this promise isn't checked and nothing is fixed automatically. Its last result stays visible.">
        {canManage
          ? <Switch id={ids.pause} aria-label="Enabled" checked={!settings.paused} disabled={saving}
            onCheckedChange={enabled => save({ paused: !enabled })} />
          : <span className="text-xs">{settings.paused ? "Disabled" : "Enabled"}</span>}
      </Row>

      <Row title={<span id={ids.within}>Should be done within</span>}
        hint={settings.withinSeconds == null
          ? `Uses the code's time limit, ${codeWithin}.`
          : `Changed here. The code says ${codeWithin}.`}>
        {canManage
          ? <div className="flex flex-col items-start gap-1.5 sm:items-end">
            <div role="radiogroup" aria-labelledby={ids.within} className="flex flex-wrap gap-1 sm:justify-end">
              {withinChoices(settings).map(choice => <button key={choice.seconds} type="button" role="radio"
                aria-checked={choice.selected} disabled={saving}
                onClick={() => { if (!choice.selected) save(withinChange(settings, choice.seconds)); }}
                className={cn("rounded-md border px-2 py-1 text-[11px] transition-colors disabled:opacity-60",
                  choice.selected ? "border-primary bg-primary/10 font-medium text-foreground" : "text-muted-foreground hover:bg-muted")}>
                {choice.label}{choice.fromCode && <span className="ml-1 text-muted-foreground">from code</span>}
              </button>)}
            </div>
            {settings.withinSeconds != null && <Button variant="link" size="sm" className="h-auto p-0 text-[11px]" disabled={saving}
              onClick={() => save({ withinSeconds: null })}>Reset to code ({codeWithin})</Button>}
          </div>
          : <span className="text-xs">{durationLabel(within)}{settings.withinSeconds == null && " (from code)"}</span>}
      </Row>

      <Row title={<label htmlFor={ids.autoFix}>Fix never-scheduled {words.many} automatically</label>}
        hint={<>
          Each check schedules the job for up to {autoFix.maxPerCheck} {words.many} that never had one. If more are found, automatic
          fixing pauses so someone can look first.{" "}
          {followsCode
            ? "Uses the code default."
            : <>Changed here; the code has it {autoFix.fromCode ? "on" : "off"}.{" "}
              {canManage && <Button variant="link" size="sm" className="h-auto p-0 text-[11px]" disabled={saving}
                onClick={() => save({ autoFix: null })}>Use the code default</Button>}</>}
        </>}>
        {canManage
          ? <Switch id={ids.autoFix} aria-label={`Fix never-scheduled ${words.many} automatically`} checked={autoFix.enabled}
            disabled={saving} onCheckedChange={enabled => save({ autoFix: enabled })} />
          : <span className="text-xs">{autoFix.enabled ? "On" : "Off"}</span>}
      </Row>
    </div>
  </section>;
}

/** What people and automatic fixing did, newest first. */
export function PromiseActivityList({ activity, formatTime }: { activity: PromiseActivity[]; formatTime: (value: string | null) => string }) {
  return <section aria-label="Activity" className="rounded-lg border">
    {activity.length === 0
      ? <p className="px-4 py-6 text-center text-xs text-muted-foreground">Nothing yet. Fixes, dismissals and setting changes appear here.</p>
      : <ol className="divide-y">
        {activity.map((a, i) => <li key={`${a.atUtc}-${i}`} className="flex gap-3 px-4 py-2 text-xs">
          <RelativeTime value={a.atUtc} formatTime={formatTime} className="w-20 shrink-0 tabular-nums text-muted-foreground" />
          <span className="min-w-0">{a.text}</span>
        </li>)}
      </ol>}
  </section>;
}
