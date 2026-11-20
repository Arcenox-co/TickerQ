import { useState } from "react";
import { AlertTriangle, ChevronRight, Power, PowerOff, Search, Wand2 } from "lucide-react";

import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { DataCard } from "@/components/cron/PageHeader";
import { TableRowsSkeleton } from "@/components/cron/TableRowsSkeleton";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import type { PromiseSnapshot } from "@/services/promise-types";
import {
  donePercentText, filterPromises, matchesFilter, paginate, promiseStatus, resultStatus, statusLabels, type PromiseFilter,
} from "@/lib/promise-list";
import { formatShort } from "@/lib/cron/format";
import { checksLate, everyLabel, nextCheckLabel } from "@/lib/relative-time";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { HealthMeter } from "./PromiseCharts";
import { Pager } from "./Pager";
import { statusTones } from "./promise-labels";
import { usePromiseActions } from "./use-promise-actions";
import { useNow } from "./use-now";

const PAGE_SIZE = 10;
const FILTERS: PromiseFilter[] = ["all", "NeedsReview", "CheckFailed", "Done", "NotChecked", "Paused"];
const COLUMNS = 8;

/** The promise's status; with <c>result</c>, what its last check found even while it is disabled. */
export function StatusBadge({ promise, result }: { promise: PromiseSnapshot; result?: boolean }) {
  const status = result ? resultStatus(promise) : promiseStatus(promise);
  return <Badge className={cn("whitespace-nowrap", statusTones[status])}>{statusLabels[status]}</Badge>;
}

function AutoFixCell({ promise }: { promise: PromiseSnapshot }) {
  const { enabled, paused } = promise.autoFix;
  if (!enabled) return <span className="text-muted-foreground">Off</span>;
  return <span className={cn("inline-flex items-center gap-1", paused ? "text-amber-700 dark:text-amber-300" : "text-foreground")}>
    <Wand2 className="size-3" aria-hidden /> {paused ? "Paused" : "On"}
  </span>;
}

/** "in 12s", "due now", "paused" or a warning when no node seems to be checking. */
export function NextCheck({ promise, formatTime }: { promise: PromiseSnapshot; formatTime: (value: string | null) => string }) {
  const now = useNow();
  const label = nextCheckLabel(promise.nextCheckAtUtc, promise.checkIntervalSeconds, now);
  const title = promise.nextCheckAtUtc
    ? `${formatTime(promise.nextCheckAtUtc)} · checked ${everyLabel(promise.checkIntervalSeconds)}`
    : "Checking is paused";
  if (label === "late") return <span title={`${title}. No node may be checking: checks run on nodes that run jobs.`}
    className="inline-flex items-center gap-1 text-amber-700 dark:text-amber-300">
    <AlertTriangle className="size-3" aria-hidden /> Late
  </span>;
  return <span title={title} className={label === "paused" ? "text-muted-foreground" : "tabular-nums"}>
    {label === "paused" ? "Paused" : label === "due now" ? "Due now" : label}
  </span>;
}

/**
 * Enabled or disabled, styled like a cron ticker's state: a pill that asks before it changes anything. An enabled
 * promise whose checks have stopped shows "Late", since no node may be checking it.
 */
function StatePill({ promise, pending, onToggle }: { promise: PromiseSnapshot; pending: boolean; onToggle: () => void }) {
  const late = checksLate(promise.nextCheckAtUtc, promise.checkIntervalSeconds, useNow());
  const enabled = !promise.settings.paused;
  const readOnly = !promise.canManage;
  const Icon = enabled && !late ? Power : PowerOff;
  return <button type="button" disabled={readOnly || pending}
    aria-label={`${enabled ? "Disable" : "Enable"} ${promise.name}`}
    onClick={e => { e.stopPropagation(); if (!readOnly && !pending) onToggle(); }}
    title={readOnly ? "Read-only mode — state cannot be changed"
      : !enabled ? "Disabled — toggle to enable"
        : late ? "Checks are late: no node may be checking this promise. Checks run on nodes that run jobs."
          : "Enabled — toggle to disable"}
    className={cn("inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-[10px] font-medium transition-colors",
      readOnly && "cursor-default",
      !enabled ? "bg-status-cancelled/10 text-status-cancelled hover:bg-status-cancelled/20"
        : late ? "bg-status-warning/10 text-status-warning hover:bg-status-warning/20"
          : "bg-status-healthy/10 text-status-healthy hover:bg-status-healthy/20")}>
    <Icon className="h-2.5 w-2.5" />
    {!enabled ? "Disabled" : late ? "Late" : "Enabled"}
  </button>;
}

/** Asks before enabling or disabling a promise, showing which one, like a cron ticker's toggle. */
function ToggleDialog({ promise, onClose }: { promise: PromiseSnapshot | null; onClose: () => void }) {
  const actions = usePromiseActions(promise);
  const enable = promise?.settings.paused ?? false;
  return <ConfirmDialog open={!!promise} onOpenChange={open => { if (!open && !actions.settings.isPending) onClose(); }}
    title={enable ? "Enable promise?" : "Disable promise?"}
    description={enable
      ? `Check this promise again ${everyLabel(promise?.checkIntervalSeconds ?? 60)}, and fix automatically where that is turned on.`
      : "Stop checking this promise. Its last result stays visible, nothing is fixed automatically, and you can enable it again later."}
    confirmLabel={enable ? "Enable" : "Disable"}
    confirmVariant={enable ? "gradient" : "default"}
    isPending={actions.settings.isPending}
    onConfirm={async () => {
      if (!promise) return;
      try { await actions.settings.mutateAsync({ paused: !enable }); onClose(); } catch { /* the toast says why */ }
    }}>
    {promise && <dl className="space-y-2 rounded-lg border border-border bg-card p-3 text-xs">
      <div className="space-y-1">
        <dt className="text-muted-foreground">Promise</dt>
        <dd className="font-medium">{promise.name}</dd>
      </div>
      <div className="space-y-1">
        <dt className="text-muted-foreground">Function</dt>
        <dd className="break-all font-mono">{promise.function}</dd>
      </div>
      <div className="space-y-1">
        <dt className="text-muted-foreground">Watches</dt>
        <dd>{promise.rowType} rows · {promise.counts.needsReview.toLocaleString()} need review</dd>
      </div>
    </dl>}
  </ConfirmDialog>;
}

/** Every promise in one dense table: status, how much is done, what needs review. A row's name opens its details. */
export function PromiseTable({ promises, loading, formatTime, onOpen }: {
  promises: PromiseSnapshot[];
  loading?: boolean;
  formatTime: (value: string | null) => string;
  onOpen: (promise: PromiseSnapshot) => void;
}) {
  const { timezone } = useTimezone();
  const [toggling, setToggling] = useState<PromiseSnapshot | null>(null);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<PromiseFilter>("all");
  const [pageNumber, setPageNumber] = useState(1);
  const page = paginate(filterPromises(promises, search, filter), pageNumber, PAGE_SIZE);
  const count = (f: PromiseFilter) => promises.filter(p => matchesFilter(p, f)).length;
  const shownFilters = FILTERS.filter(f => f === "all" || f === filter || count(f) > 0);

  return <DataCard className="overflow-hidden">
    <div className="flex flex-wrap items-center gap-2 border-b px-4 py-2.5">
      <div className="relative w-full max-w-xs">
        <Search className="absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden />
        <Input value={search} placeholder="Search promise, job or row type" aria-label="Search promises" className="h-8 pl-8 text-xs"
          onChange={e => { setSearch(e.target.value); setPageNumber(1); }} />
      </div>
      <div className="flex flex-wrap gap-1" role="group" aria-label="Filter by status">
        {shownFilters.map(f => <button key={f} type="button" aria-pressed={filter === f}
          onClick={() => { setFilter(f); setPageNumber(1); }}
          className={cn("rounded-md px-2.5 py-1 text-xs tabular-nums transition-colors",
            filter === f ? "bg-primary/10 font-medium text-foreground" : "text-muted-foreground hover:bg-muted")}>
          {f === "all" ? "All" : statusLabels[f]} <span className="text-muted-foreground">{count(f)}</span>
        </button>)}
      </div>
    </div>

    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead className="bg-muted/30 text-left text-[11px] text-muted-foreground">
          <tr className="border-b">
            <th className="px-4 py-2 font-medium">Promise</th>
            <th className="px-3 py-2 font-medium">State</th>
            <th className="px-3 py-2 font-medium">Status</th>
            <th className="min-w-48 px-3 py-2 font-medium">Done</th>
            <th className="px-3 py-2 text-right font-medium">Needs review</th>
            <th className="px-3 py-2 font-medium">Auto-fix</th>
            <th className="px-3 py-2 font-medium">Last checked</th>
            <th className="w-8"><span className="sr-only">Open</span></th>
          </tr>
        </thead>
        <tbody>
          {loading && <TableRowsSkeleton rows={4} cols={COLUMNS} />}
          {!loading && page.items.map(promise =>
            <tr key={promise.id} onClick={() => onOpen(promise)}
              className="cursor-pointer border-b last:border-b-0 hover:bg-muted/40">
              <td className="px-4 py-2.5">
                <button type="button" onClick={e => { e.stopPropagation(); onOpen(promise); }}
                  className="rounded-sm text-left font-medium hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
                  {promise.name}
                </button>
                <p className="mt-0.5 text-[11px] text-muted-foreground">
                  <span className="font-mono">{promise.function}</span> · {promise.rowType}
                </p>
              </td>
              <td className="px-3 py-2.5"><StatePill promise={promise} pending={toggling?.id === promise.id}
                onToggle={() => setToggling(promise)} /></td>
              <td className="px-3 py-2.5"><StatusBadge promise={promise} result /></td>
              <td className="px-3 py-2.5">
                <div className="flex items-center gap-2.5">
                  <HealthMeter counts={promise.counts} className="h-1.5 w-28" />
                  <span className="whitespace-nowrap tabular-nums">
                    {donePercentText(promise.counts)}
                    <span className="ml-1 text-[11px] text-muted-foreground">
                      {promise.counts.done.toLocaleString()}/{promise.counts.watched.toLocaleString()}
                    </span>
                  </span>
                </div>
              </td>
              <td className={cn("px-3 py-2.5 text-right tabular-nums", promise.counts.needsReview > 0 && "font-semibold text-destructive")}>
                {promise.counts.needsReview.toLocaleString()}
              </td>
              <td className="px-3 py-2.5"><AutoFixCell promise={promise} /></td>
              <td className="whitespace-nowrap px-3 py-2.5 tabular-nums text-muted-foreground" title={formatTime(promise.checkedAtUtc)}>
                {promise.checkedAtUtc ? formatShort(promise.checkedAtUtc, timezone) : "Not yet"}
              </td>
              <td className="pr-3 text-muted-foreground"><ChevronRight className="size-4" aria-hidden /></td>
            </tr>)}
          {!loading && page.total === 0 &&
            <tr><td colSpan={COLUMNS} className="px-4 py-8 text-center text-muted-foreground">No promise matches.</td></tr>}
        </tbody>
      </table>
    </div>
    {!loading && <Pager page={page} noun="promises" onPage={setPageNumber} />}
    <ToggleDialog promise={toggling} onClose={() => setToggling(null)} />
  </DataCard>;
}
