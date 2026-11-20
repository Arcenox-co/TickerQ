import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowUpRight, CheckCircle2, Loader2 } from "lucide-react";

import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { STATUS_CONFIG } from "@/lib/cron/status-config";
import type { PromiseFinding, PromiseFindingKind } from "@/services/promise-types";
import {
  findingsFor, fixLabel, groupFindings, isOpen, isReportOnly, noFixReason, type FindingTab, type Nouns,
} from "@/lib/promise-list";
import { cn } from "@/lib/utils";
import { groupTitle, jobStatusLabels, kindGuides, kindIcons, kindTones, type GuideContext } from "./promise-labels";
import { RelativeTime } from "./RelativeTime";
import type { PromiseActions } from "./use-promise-actions";

// A group shows this many rows until it is expanded.
const GROUP_SIZE = 5;

type FormatTime = (value: string | null) => string;

/**
 * The rows that need a person, grouped by why: each group says once what happened and what to do, and each row adds
 * only its own detail, its job and one action. Dismissed rows sit apart.
 */
export function PromiseFindings({ findings, words, fn, doneWhen, canManage, actions, formatTime }: {
  findings: PromiseFinding[];
  words: Nouns;
  /** The promise's function and DoneWhen in words, for the groups' guidance. */
  fn: string;
  doneWhen: string | null;
  canManage: boolean;
  actions: PromiseActions;
  formatTime: FormatTime;
}) {
  const [tab, setTab] = useState<FindingTab>("open");
  const [dismissing, setDismissing] = useState<PromiseFinding | null>(null);
  const openCount = findings.filter(isOpen).length;
  const dismissedCount = findings.length - openCount;
  const groups = groupFindings(findingsFor(findings, tab, null));

  return <div className="space-y-3">
    {dismissedCount > 0 && <div role="tablist" aria-label="Show" className="inline-flex rounded-md border p-0.5 text-[11px]">
      {(["open", "dismissed"] as const).map(t => <button key={t} type="button" role="tab" aria-selected={tab === t}
        onClick={() => setTab(t)}
        className={cn("rounded px-2.5 py-1 tabular-nums transition-colors",
          tab === t ? "bg-muted font-medium text-foreground" : "text-muted-foreground hover:text-foreground")}>
        {t === "open" ? `To review ${openCount}` : `Dismissed ${dismissedCount}`}
      </button>)}
    </div>}

    {groups.length === 0
      ? <div className="flex flex-col items-center gap-1.5 rounded-lg border border-dashed px-4 py-10 text-center">
        <CheckCircle2 className="size-5 text-status-healthy" aria-hidden />
        <p className="text-sm font-medium">{tab === "open" ? "Nothing to review" : "Nothing dismissed"}</p>
        {tab === "open" && <p className="max-w-sm text-xs text-muted-foreground">
          Every watched {words.one} is done or on its way. Failed jobs are retried and handled on Executions.
        </p>}
      </div>
      : groups.map(group => <FindingGroup key={group.kind} kind={group.kind} findings={group.findings} guide={{ words, fn, doneWhen }}
        canManage={canManage && tab === "open"} actions={actions} formatTime={formatTime} onDismiss={setDismissing} />)}

    <DismissDialog finding={dismissing} words={words} actions={actions} onClose={() => setDismissing(null)} />
  </div>;
}

function FindingGroup({ kind, findings, guide, canManage, actions, formatTime, onDismiss }: {
  kind: PromiseFindingKind;
  findings: PromiseFinding[];
  guide: GuideContext;
  canManage: boolean;
  actions: PromiseActions;
  formatTime: FormatTime;
  onDismiss: (finding: PromiseFinding) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const Icon = kindIcons[kind];
  const shown = expanded ? findings : findings.slice(0, GROUP_SIZE);

  const title = groupTitle(kind, guide.words);
  return <section aria-label={title} className="overflow-hidden rounded-lg border">
    <header className="flex gap-3 border-b bg-muted/30 px-4 py-3">
      <span className={cn("mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full", kindTones[kind])}>
        <Icon className="size-3.5" aria-hidden />
      </span>
      <div className="min-w-0 space-y-0.5">
        <h3 className="flex flex-wrap items-center gap-2 text-sm font-medium">
          {title}
          <span className="rounded-full bg-muted px-1.5 text-[11px] font-normal tabular-nums text-muted-foreground">{findings.length}</span>
          {isReportOnly(kind) && <span className="text-[11px] font-normal text-muted-foreground">· needs a person</span>}
        </h3>
        <p className="text-xs leading-relaxed text-muted-foreground">{kindGuides[kind](guide)}</p>
      </div>
    </header>
    <ul className="divide-y">
      {shown.map(finding => <FindingRow key={finding.id} finding={finding} canManage={canManage} actions={actions}
        formatTime={formatTime} onDismiss={() => onDismiss(finding)} />)}
    </ul>
    {findings.length > GROUP_SIZE && <button type="button" onClick={() => setExpanded(!expanded)}
      className="w-full border-t px-4 py-2 text-left text-[11px] text-primary hover:bg-muted/40">
      {expanded ? "Show fewer" : `Show ${findings.length - GROUP_SIZE} more`}
    </button>}
  </section>;
}

function FindingRow({ finding, canManage, actions, formatTime, onDismiss }: {
  finding: PromiseFinding;
  canManage: boolean;
  actions: PromiseActions;
  formatTime: FormatTime;
  onDismiss: () => void;
}) {
  const label = fixLabel(finding);
  const why = noFixReason(finding);
  const fixing = actions.fix.isPending && actions.fix.variables === finding.id;
  const job = finding.lastJob;

  return <li className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-start sm:gap-4">
    <div className="min-w-0 flex-1 space-y-1">
      <p className="flex flex-wrap items-baseline gap-x-2 text-sm">
        <span className="font-medium">{finding.label}</span>
        {finding.key !== finding.label && <span className="font-mono text-[11px] text-muted-foreground">{finding.key}</span>}
      </p>
      <p className="text-xs text-muted-foreground">{finding.reason}</p>
      <p className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5 text-[11px] text-muted-foreground">
        {job && <>
          <span className={cn("inline-flex items-center gap-1", STATUS_CONFIG[job.status].text)}>
            <span className={cn("size-1.5 rounded-full", STATUS_CONFIG[job.status].dot)} aria-hidden />
            {jobStatusLabels[job.status]}
          </span>
          <span aria-hidden>·</span>
          <Link to={`/time-tickers?ticker=${job.id}`} className="inline-flex items-center gap-0.5 hover:text-foreground hover:underline">
            job <span className="font-mono">{job.id.slice(0, 8)}</span><ArrowUpRight className="size-3" aria-hidden />
          </Link>
          <span aria-hidden>·</span>
        </>}
        <span>found <RelativeTime value={finding.detectedAtUtc} formatTime={formatTime} /></span>
      </p>
      {job?.error && <p title={job.error} className={cn("truncate font-mono text-[11px]",
        job.status === "Failed" ? "text-destructive" : "text-muted-foreground")}>{job.error}</p>}
      {canManage && why && <p className="text-[11px] text-muted-foreground">{why}</p>}
      {finding.dismissed && <p className="text-[11px] text-muted-foreground">
        Dismissed by <span className="text-foreground">{finding.dismissed.by}</span>{" "}
        <RelativeTime value={finding.dismissed.at} formatTime={formatTime} />
        {finding.dismissed.reason && <> — “{finding.dismissed.reason}”</>}
      </p>}
    </div>
    {canManage && <div className="flex shrink-0 items-center gap-1.5 sm:pt-0.5">
      {label && <Button size="sm" className="h-7" disabled={actions.fix.isPending} onClick={() => actions.fix.mutate(finding.id)}>
        {fixing && <Loader2 className="mr-1 size-3 animate-spin" />}{label}
      </Button>}
      <Button size="sm" variant={label ? "ghost" : "outline"} className="h-7 text-muted-foreground" onClick={onDismiss}>Dismiss</Button>
    </div>}
  </li>;
}

function DismissDialog({ finding, words, actions, onClose }: {
  finding: PromiseFinding | null;
  words: Nouns;
  actions: PromiseActions;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  const close = () => { setReason(""); onClose(); };
  return <ConfirmDialog open={!!finding} onOpenChange={open => { if (!open) close(); }}
    title={finding ? `Dismiss ${finding.label}?` : `Dismiss this ${words.one}?`}
    description={`It leaves the review list and nothing is scheduled. If this ${words.one} runs into a new problem, it comes back.`}
    confirmLabel="Dismiss" isPending={actions.dismiss.isPending}
    onConfirm={async () => {
      if (!finding) return;
      try {
        await actions.dismiss.mutateAsync({ findingId: finding.id, reason: reason.trim() || undefined });
        close();
      } catch { /* the toast says why; the dialog stays open */ }
    }}>
    <div className="space-y-1.5">
      <Label htmlFor="dismiss-reason">Why? (optional)</Label>
      <Input id="dismiss-reason" value={reason} maxLength={500} placeholder="For example: customer paid by bank transfer"
        onChange={e => setReason(e.target.value)} />
      <p className="text-[11px] text-muted-foreground">Shown to everyone who opens this promise.</p>
    </div>
  </ConfirmDialog>;
}
