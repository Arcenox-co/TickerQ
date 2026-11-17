import { useState } from "react";
import { Link } from "react-router-dom";
import { Code2, Loader2, PauseCircle, RefreshCw, SlidersHorizontal, Wand2 } from "lucide-react";

import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import type { PromiseSnapshot } from "@/services/promise-types";
import { countOf, donePercentText, FIX_ALL_LIMIT, fixable, isOpen, nouns, type Nouns } from "@/lib/promise-list";
import { checksLate, everyLabel } from "@/lib/relative-time";
import { HealthMeter, Stat, Swatch } from "./PromiseCharts";
import { HEALTH_LABELS } from "./promise-labels";
import { PromiseFindings } from "./PromiseFindings";
import { PromiseJobs } from "./PromiseJobs";
import { PromiseActivityList } from "./PromiseSettings";
import { PromiseSetup } from "./PromiseSetup";
import { NextCheck, StatusBadge } from "./PromiseTable";
import { RelativeTime } from "./RelativeTime";
import { useNow } from "./use-now";
import { usePromiseActions } from "./use-promise-actions";

type FormatTime = (value: string | null) => string;
type Tab = "review" | "jobs" | "activity";

/** One promise: how it is doing, the rows that need review, the jobs that ran, what happened, and its settings. */
export function PromiseDetailSheet({ promise, formatTime, onClose }: {
  promise: PromiseSnapshot | null;
  formatTime: FormatTime;
  onClose: () => void;
}) {
  return <Sheet open={!!promise} onOpenChange={open => { if (!open) onClose(); }}>
    <SheetContent className="gap-0 overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-4xl">
      {promise && <Details key={promise.id} promise={promise} formatTime={formatTime} />}
    </SheetContent>
  </Sheet>;
}

function Details({ promise, formatTime }: { promise: PromiseSnapshot; formatTime: FormatTime }) {
  const actions = usePromiseActions(promise);
  const [confirmAll, setConfirmAll] = useState(false);
  const [setupOpen, setSetupOpen] = useState(false);
  const [tab, setTab] = useState<Tab>(promise.counts.needsReview > 0 ? "review" : "jobs");
  const words = nouns(promise.rowType);
  const toFix = fixable(promise.findings).length;
  const willFix = Math.min(toFix, FIX_ALL_LIMIT);
  const { canManage, counts } = promise;
  const openFindings = promise.findings.filter(isOpen).length;

  return <>
    <SheetHeader className="gap-3 border-b px-5 pb-4 pt-5">
      <div className="flex flex-wrap items-start justify-between gap-3 pr-8">
        <div className="min-w-0 space-y-1.5">
          <div className="flex flex-wrap items-center gap-2">
            <SheetTitle className="text-base">{promise.name}</SheetTitle>
            <StatusBadge promise={promise} />
          </div>
          <SheetDescription asChild>
            <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
              <span className="font-mono text-foreground/80">{promise.function}</span>
              <span aria-hidden>·</span>
              <span>{promise.rowType} rows</span>
              <span aria-hidden>·</span>
              <span className="inline-flex items-center gap-1"><Code2 className="size-3" aria-hidden /> Declared in code</span>
            </p>
          </SheetDescription>
        </div>
        <div className="flex shrink-0 gap-2">
          <Button size="sm" variant="ghost" onClick={() => setSetupOpen(true)} title="Rule from code and dashboard settings">
            <SlidersHorizontal className="mr-1.5 size-3.5" /> Setup
          </Button>
          {canManage && <Button size="sm" variant="outline" disabled={actions.check.isPending} onClick={() => actions.check.mutate()}>
            {actions.check.isPending ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <RefreshCw className="mr-1.5 size-3.5" />}
            Check now
          </Button>}
          {canManage && toFix > 0 && <Button size="sm" disabled={actions.fixAll.isPending} onClick={() => setConfirmAll(true)}>
            <Wand2 className="mr-1.5 size-3.5" /> Fix all ({willFix})
          </Button>}
        </div>
      </div>
      <p className="text-xs leading-relaxed text-muted-foreground">{promise.summary}</p>
    </SheetHeader>

    <div className="space-y-4 px-5 py-4">
      <Notices promise={promise} words={words} formatTime={formatTime} onOpenSetup={() => setSetupOpen(true)}
        onResume={canManage ? () => actions.settings.mutate({ autoFix: true }) : undefined} resuming={actions.settings.isPending} />
      <Overview promise={promise} words={words} formatTime={formatTime} />

      <Tabs value={tab} onValueChange={value => setTab(value as Tab)} className="gap-3">
        <TabsList variant="line" className="w-full justify-start gap-4 border-b px-0">
          <TabsTrigger value="review" className="flex-none px-0 text-xs">
            Needs review {counts.needsReview > 0 && <Count tone="error">{counts.needsReview}</Count>}
          </TabsTrigger>
          <TabsTrigger value="jobs" className="flex-none px-0 text-xs">Jobs</TabsTrigger>
          <TabsTrigger value="activity" className="flex-none px-0 text-xs">
            Activity {promise.activity.length > 0 && <Count>{promise.activity.length}</Count>}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="review" className="space-y-2">
          <PromiseFindings findings={promise.findings} words={words} fn={promise.function} doneWhen={promise.doneWhen}
            canManage={canManage} actions={actions} formatTime={formatTime} />
          {counts.needsReview > openFindings && <p className="text-[11px] text-muted-foreground">
            The list shows the newest {openFindings} of {counts.needsReview.toLocaleString()} {words.many}; fix or dismiss them to see
            the rest after the next check.
          </p>}
        </TabsContent>
        <TabsContent value="jobs"><PromiseJobs promise={promise} /></TabsContent>
        <TabsContent value="activity"><PromiseActivityList activity={promise.activity} formatTime={formatTime} /></TabsContent>
      </Tabs>
    </div>

    <PromiseSetup promise={promise} words={words} actions={actions} open={setupOpen} onOpenChange={setSetupOpen} />

    <ConfirmDialog open={confirmAll} onOpenChange={setConfirmAll}
      title={`Run the fix for ${countOf(willFix, words)}?`}
      description={<>
        The promise is checked again first, then each {words.one} that still needs it gets its fix: a new job, or its waiting
        job runs now. Dismissed {words.many} and {words.many} whose job already ran are left alone.
        {toFix > FIX_ALL_LIMIT && ` One run fixes at most ${FIX_ALL_LIMIT}; run it again for the rest.`}
      </>}
      confirmLabel={`Fix ${countOf(willFix, words)}`} isPending={actions.fixAll.isPending}
      onConfirm={async () => {
        try { await actions.fixAll.mutateAsync(); setConfirmAll(false); } catch { /* the toast says why */ }
      }} />
  </>;
}

function Count({ tone, children }: { tone?: "error"; children: React.ReactNode }) {
  return <span className={`ml-1.5 rounded-full px-1.5 text-[10px] tabular-nums ${tone === "error"
    ? "bg-destructive/10 text-destructive" : "bg-muted text-muted-foreground"}`}>{children}</span>;
}

function Notice({ tone, children }: { tone: "error" | "warn" | "info"; children: React.ReactNode }) {
  const tones = {
    error: "border-destructive/30 bg-destructive/10 text-destructive",
    warn: "border-amber-500/30 bg-amber-500/10 text-amber-800 dark:text-amber-300",
    info: "border-border bg-muted/40 text-muted-foreground",
  };
  return <div role={tone === "error" ? "alert" : undefined} className={`rounded-md border px-3 py-2 text-xs ${tones[tone]}`}>{children}</div>;
}

function Notices({ promise, words, formatTime, onResume, resuming, onOpenSetup }: {
  promise: PromiseSnapshot;
  words: Nouns;
  formatTime: FormatTime;
  onOpenSetup: () => void;
  onResume?: () => void;
  resuming: boolean;
}) {
  const { counts } = promise;
  const now = useNow();
  return <>
    {checksLate(promise.nextCheckAtUtc, promise.checkIntervalSeconds, now) && <Notice tone="warn">
      <strong className="font-medium">Checks are late.</strong> This promise is checked {everyLabel(promise.checkIntervalSeconds)}, but
      the last check was <RelativeTime value={promise.checkedAtUtc} formatTime={formatTime} />. Checks run on nodes that run jobs —
      make sure one is up and its scheduler is started. Check now runs one on this node.
    </Notice>}
    {promise.settings.paused && <Notice tone="info">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span>
          <PauseCircle className="mr-1 inline size-3.5 align-[-2px]" aria-hidden />
          This promise is disabled, so it isn't checked and what you see is from its last check.
        </span>
        <Button size="sm" variant="outline" className="h-7 bg-transparent" onClick={onOpenSetup}>Open setup</Button>
      </div>
    </Notice>}
    {promise.error && <Notice tone="error">{promise.error}</Notice>}
    {promise.autoFix.paused && <Notice tone="warn">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span><strong className="font-medium">Automatic fixing paused.</strong> {promise.autoFix.pausedReason}</span>
        {onResume && <Button size="sm" variant="outline" className="h-7 bg-transparent" disabled={resuming} onClick={onResume}>
          Resume automatic fixing
        </Button>}
      </div>
    </Notice>}
    {promise.truncated && <Notice tone="warn">
      Only the newest {words.many} were checked: there were more {words.many} or jobs than one check reads, so older ones
      may be missing. Nothing is reported as never scheduled for want of a job it didn't read.
    </Notice>}
    {counts.unmatchable > 0 && <Notice tone="warn">
      {countOf(counts.unmatchable, words)} {counts.unmatchable === 1 ? "has" : "have"} no value to match a job by, so{" "}
      {counts.unmatchable === 1 ? "it" : "they"} can't be checked. A developer should look at the promise's matchBy.
    </Notice>}
  </>;
}

function Overview({ promise, words, formatTime }: { promise: PromiseSnapshot; words: Nouns; formatTime: FormatTime }) {
  const { counts } = promise;
  return <section aria-label="Overview" className="space-y-2.5">
    <dl className="grid grid-cols-2 overflow-hidden rounded-lg border sm:grid-cols-5 sm:divide-x">
      <Stat label="Done" value={donePercentText(counts)} hint={`${counts.done.toLocaleString()} of ${countOf(counts.watched, words)}`}
        swatch="done" />
      <Stat label={HEALTH_LABELS.running} value={counts.running.toLocaleString()} swatch="running" />
      <Stat label={HEALTH_LABELS.needsReview} value={counts.needsReview.toLocaleString()} swatch="needsReview"
        tone={counts.needsReview > 0 ? "error" : undefined}
        hint={counts.duplicates > 0 ? `${counts.duplicates.toLocaleString()} ran twice` : undefined} />
      <Stat label="Failed" value={counts.failed.toLocaleString()} swatch="failed" hint="see Executions" />
      <Stat label="Dismissed" value={counts.dismissed.toLocaleString()} swatch="other" />
    </dl>
    <HealthMeter counts={counts} words={words} />
    <p className="flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-muted-foreground">
      <span>
        Checked {promise.checkedAtUtc ? <RelativeTime value={promise.checkedAtUtc} formatTime={formatTime} className="text-foreground" /> : "not yet"}
        {promise.durationMs != null && ` in ${promise.durationMs.toLocaleString()} ms`}
      </span>
      <span>Next check: <NextCheck promise={promise} formatTime={formatTime} /> · {everyLabel(promise.checkIntervalSeconds)}</span>
      {promise.windowStartUtc && <span title={formatTime(promise.windowStartUtc)}>
        Looks back to {words.many} from <RelativeTime value={promise.windowStartUtc} formatTime={formatTime} />
      </span>}
      {counts.failed > 0 && <span className="inline-flex items-center gap-1">
        <Swatch part="failed" /> Failed jobs are retried and handled on{" "}
        <Link to="/executions" className="text-primary hover:underline">Executions</Link>
      </span>}
    </p>
  </section>;
}
