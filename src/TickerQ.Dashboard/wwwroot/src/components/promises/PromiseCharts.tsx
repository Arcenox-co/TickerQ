import { Link } from "react-router-dom";
import { AlertTriangle, CheckCircle2, Eye, Handshake } from "lucide-react";

import { DataCard } from "@/components/cron/PageHeader";
import { Skeleton } from "@/components/ui/skeleton";
import {
  countOf, donePercentText, healthParts, kindLabels, missedByKind, ROWS, totalCounts,
  type HealthKey, type Nouns,
} from "@/lib/promise-list";
import { HEALTH_LABELS, PROMISE_COLORS } from "./promise-labels";
import type { PromiseCounts, PromiseSnapshot } from "@/services/promise-types";
import { cn } from "@/lib/utils";

/**
 * Part-to-whole bar of the watched rows: done, running, needs review, failed, the rest. Segments are separated by a
 * 2px surface gap and only the outer ends are rounded.
 */
export function HealthMeter({ counts, words = ROWS, className }: { counts: PromiseCounts; words?: Nouns; className?: string }) {
  const parts = healthParts(counts);
  const label = `${countOf(counts.watched, words)} watched: ` +
    parts.filter(p => p.value > 0).map(p => `${HEALTH_LABELS[p.key]} ${p.value}`).join(", ");
  return <div role="img" aria-label={label} className={cn("flex h-2 w-full gap-[2px] overflow-hidden rounded-[4px] bg-muted", className)}>
    {parts.filter(p => p.value > 0).map(p => <span key={p.key} title={`${HEALTH_LABELS[p.key]}: ${p.value.toLocaleString()}`}
      style={{ flexGrow: p.value, flexBasis: 0, background: PROMISE_COLORS[p.key], minWidth: 3 }} />)}
  </div>;
}

/** One number in a strip of numbers: a label, the value, and an optional line under it. */
export function Stat({ label, value, hint, tone, swatch }: {
  label: string;
  value: string;
  hint?: string;
  tone?: "error";
  swatch?: HealthKey;
}) {
  return <div className="min-w-0 space-y-0.5 border-b px-3 py-2.5 sm:border-b-0">
    <dt className="flex items-center gap-1.5 text-[11px] text-muted-foreground">{swatch && <Swatch part={swatch} />}{label}</dt>
    <dd className={cn("text-base font-semibold tabular-nums", tone === "error" && "text-destructive")}>{value}</dd>
    {hint && <dd className="truncate text-[11px] text-muted-foreground">{hint}</dd>}
  </div>;
}

export function Swatch({ part }: { part: HealthKey }) {
  return <span aria-hidden className="inline-block size-2 shrink-0 rounded-[2px]" style={{ background: PROMISE_COLORS[part] }} />;
}

/**
 * The page's headline in one slim strip: how many promises, rows watched, how much is done, and what needs review by
 * why, with the watched rows' split as a bar underneath.
 */
export function PromiseKpis({ promises }: { promises: PromiseSnapshot[] }) {
  const sum = totalCounts(promises);
  const paused = promises.filter(p => p.settings.paused).length;
  const parts = healthParts(sum).filter(p => p.value > 0);
  const kinds = missedByKind(promises).filter(k => k.count > 0);
  return <DataCard>
    <dl className="grid grid-cols-2 sm:grid-cols-[repeat(3,minmax(0,1fr))_minmax(0,2fr)] sm:divide-x">
      <Headline icon={Handshake} label="Promises" value={promises.length.toLocaleString()}
        detail={paused ? `${promises.length - paused} enabled · ${paused} disabled` : "All enabled"} />
      <Headline icon={Eye} label="Rows watched" value={sum.watched.toLocaleString()}
        detail={`${sum.running.toLocaleString()} with a job running`} />
      <Headline icon={CheckCircle2} label="Done" value={donePercentText(sum)}
        detail={`${sum.done.toLocaleString()} of ${countOf(sum.watched, ROWS)}`} />
      <div className="col-span-2 min-w-0 space-y-1 px-4 py-3 sm:col-span-1">
        <dt className="flex items-center gap-1.5 text-xs text-muted-foreground"><AlertTriangle className="size-3.5" aria-hidden /> Needs review</dt>
        <dd className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
          <span className={cn("text-xl font-semibold tabular-nums", sum.needsReview > 0 && "text-destructive")}>
            {sum.needsReview.toLocaleString()}
          </span>
          {kinds.length === 0
            ? <span className="text-[11px] text-muted-foreground">Nothing was missed or ran twice</span>
            : kinds.map(k => <span key={k.kind} className="text-[11px] text-muted-foreground">
              {kindLabels[k.kind]} <span className="tabular-nums text-foreground">{k.count.toLocaleString()}</span>
            </span>)}
        </dd>
        {sum.failed > 0 && <dd className="text-[11px] text-muted-foreground">
          {sum.failed.toLocaleString()} more failed · <Link to="/executions" className="text-primary hover:underline">Executions</Link>
        </dd>}
      </div>
    </dl>
    <div className="flex flex-col gap-2 border-t px-4 py-2.5 sm:flex-row sm:items-center">
      <HealthMeter counts={sum} className="sm:flex-1" />
      <ul className="flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-muted-foreground">
        {parts.map(p => <li key={p.key} className="inline-flex items-center gap-1.5">
          <Swatch part={p.key} /> {HEALTH_LABELS[p.key]} <span className="tabular-nums text-foreground">{p.value.toLocaleString()}</span>
        </li>)}
      </ul>
    </div>
  </DataCard>;
}

function Headline({ icon: Icon, label, value, detail }: {
  icon: typeof Handshake;
  label: string;
  value: string;
  detail?: React.ReactNode;
}) {
  return <div className="min-w-0 space-y-1 border-b px-4 py-3 sm:border-b-0">
    <dt className="flex items-center gap-1.5 text-xs text-muted-foreground"><Icon className="size-3.5" aria-hidden /> {label}</dt>
    <dd className="text-xl font-semibold tabular-nums tracking-tight">{value}</dd>
    {detail && <dd className="truncate text-[11px] text-muted-foreground">{detail}</dd>}
  </div>;
}

export function KpiSkeleton() {
  return <DataCard className="grid grid-cols-2 gap-4 p-4 sm:grid-cols-4">
    {[0, 1, 2, 3].map(i => <div key={i} className="space-y-2">
      <Skeleton className="h-3 w-24" /><Skeleton className="h-6 w-16" /><Skeleton className="h-3 w-32" />
    </div>)}
  </DataCard>;
}
