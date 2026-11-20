import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowUpRight, Bot, Search, UserRound } from "lucide-react";

import { StatusBadge } from "@/components/cron/StatusBadges";
import { QueryErrorNotice } from "@/components/cron/QueryErrorNotice";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDuration, formatShort } from "@/lib/cron/format";
import {
  averageDuration, filterJobs, hasRun, JOB_FILTERS, jobFilterCount, jobFilterLabels, jobSourceLabel, successRate, windowTotal,
  type JobFilter,
} from "@/lib/promise-jobs";
import { paginate } from "@/lib/promise-list";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { usePromiseJobs } from "@/services/hooks";
import type { PromiseJob, PromiseSnapshot } from "@/services/promise-types";
import { Pager } from "./Pager";
import { RelativeTime } from "./RelativeTime";
import { Stat } from "./PromiseCharts";

const PAGE_SIZE = 10;
const COLUMNS = 7;

/** The function's jobs in the promise's window: what ran, how long it took, what failed, and which ones a fix created. */
export function PromiseJobs({ promise }: { promise: PromiseSnapshot }) {
  const query = usePromiseJobs(promise.id);
  const { timezone } = useTimezone();
  const [filter, setFilter] = useState<JobFilter>("all");
  const [search, setSearch] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  // A job's key names the row; findings know the row's friendlier label.
  const labels = new Map(promise.findings.map(f => [f.key, f.label]));

  if (query.isError) return <QueryErrorNotice message="Jobs could not be loaded." onRetry={() => void query.refetch()} />;
  if (!query.data) return <div className="space-y-3">
    <Skeleton className="h-16" />
    {[0, 1, 2, 3].map(i => <Skeleton key={i} className="h-9" />)}
  </div>;

  const list = query.data;
  const page = paginate(filterJobs(list.jobs, filter, search), pageNumber, PAGE_SIZE);
  const total = windowTotal(list);
  const rate = successRate(list);
  const average = averageDuration(list.jobs);
  const time = (value: string | null) => formatShort(value, timezone);

  return <div className="space-y-3">
    <dl className="grid grid-cols-2 divide-border overflow-hidden rounded-lg border sm:grid-cols-5 sm:divide-x">
      <Stat label="Jobs" value={total.toLocaleString()} hint={`since ${time(list.windowStartUtc)}`} />
      <Stat label="Succeeded" value={rate == null ? "—" : `${rate}%`} hint={`${list.totals.done.toLocaleString()} done`} />
      <Stat label="Failed" value={list.totals.failed.toLocaleString()} tone={list.totals.failed > 0 ? "error" : undefined} />
      <Stat label="Waiting or running" value={(list.totals.waiting + list.totals.running).toLocaleString()}
        hint={list.totals.stopped > 0 ? `${list.totals.stopped.toLocaleString()} cancelled or skipped` : undefined} />
      <Stat label="Average run" value={average == null ? "—" : formatDuration(average)} hint="of the listed jobs" />
    </dl>

    <div className="overflow-hidden rounded-lg border">
      <div className="flex flex-wrap items-center gap-2 border-b bg-muted/30 px-3 py-2">
        <div role="group" aria-label="Filter jobs" className="flex flex-wrap gap-1">
          {JOB_FILTERS.map(f => <button key={f} type="button" aria-pressed={filter === f}
            onClick={() => { setFilter(f); setPageNumber(1); }}
            className={cn("rounded-md px-2.5 py-1 text-[11px] tabular-nums transition-colors",
              filter === f ? "bg-background font-medium text-foreground shadow-sm" : "text-muted-foreground hover:bg-muted")}>
            {jobFilterLabels[f]} <span className="text-muted-foreground">{jobFilterCount(list.jobs, f)}</span>
          </button>)}
        </div>
        <div className="relative ml-auto w-full sm:w-52">
          <Search className="absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <Input value={search} placeholder="Search jobs" aria-label="Search jobs" className="h-7 pl-8 text-xs"
            onChange={e => { setSearch(e.target.value); setPageNumber(1); }} />
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-left text-[11px] text-muted-foreground">
            <tr className="border-b">
              <th className="px-3 py-2 font-medium">Status</th>
              <th className="px-3 py-2 font-medium">Row</th>
              <th className="px-3 py-2 font-medium">Scheduled by</th>
              <th className="px-3 py-2 font-medium">Due</th>
              <th className="px-3 py-2 font-medium">Ran</th>
              <th className="px-3 py-2 text-right font-medium">Took</th>
              <th className="w-8"><span className="sr-only">Open</span></th>
            </tr>
          </thead>
          <tbody>
            {page.items.map(job => <JobRow key={job.id} job={job} label={(job.key ? labels.get(job.key) : undefined) ?? job.label ?? undefined}
              time={time} />)}
            {page.total === 0 && <tr><td colSpan={COLUMNS} className="px-4 py-8 text-center text-muted-foreground">
              {list.jobs.length === 0 ? `No ${promise.function} jobs since ${time(list.windowStartUtc)}.` : "No job matches."}
            </td></tr>}
          </tbody>
        </table>
      </div>
      <Pager page={page} noun="jobs" onPage={setPageNumber} />
    </div>
    {total > list.jobs.length && <p className="text-[11px] text-muted-foreground">
      The table lists the newest {list.jobs.length} of {total.toLocaleString()} jobs. The totals above count them all.
    </p>}
  </div>;
}

function JobRow({ job, label, time }: { job: PromiseJob; label?: string; time: (value: string | null) => string }) {
  const SourceIcon = job.source === "Fix" ? UserRound : job.source === "AutoFix" ? Bot : null;
  return <>
    <tr className={cn("border-b last:border-b-0 hover:bg-muted/40", job.error && "border-b-0")}>
      <td className="px-3 py-2"><StatusBadge status={job.status} label={job.status === "Idle" ? "Waiting" : undefined} /></td>
      <td className="max-w-48 px-3 py-2">
        <p className="truncate font-medium" title={label ?? job.key ?? undefined}>{label ?? job.key ?? "—"}</p>
        {label && label !== job.key && <p className="truncate font-mono text-[11px] text-muted-foreground">{job.key}</p>}
      </td>
      <td className="whitespace-nowrap px-3 py-2">
        <span className={cn("inline-flex items-center gap-1", job.source === "App" ? "text-muted-foreground" : "text-foreground")}>
          {SourceIcon && <SourceIcon className="size-3" aria-hidden />}
          {jobSourceLabel(job)}
        </span>
      </td>
      <td className="whitespace-nowrap px-3 py-2 tabular-nums text-muted-foreground">
        <RelativeTime value={job.executionTimeUtc ?? job.createdAtUtc} formatTime={time} />
      </td>
      <td className="whitespace-nowrap px-3 py-2 tabular-nums">
        {hasRun(job) ? <RelativeTime value={job.executedAtUtc} formatTime={time} /> : <span className="text-muted-foreground">
          {job.status === "Cancelled" || job.status === "Skipped" ? "Never" : "Not yet"}
        </span>}
        {job.retryCount > 0 && <span className="ml-1.5 text-[11px] text-muted-foreground">retry {job.retryCount}/{job.retries}</span>}
      </td>
      <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">{hasRun(job) ? formatDuration(job.elapsedMs) : "—"}</td>
      <td className="pr-2">
        <Link to={`/time-tickers?ticker=${job.id}`} aria-label={`Open job ${job.id.slice(0, 8)}`} title="Open job"
          className="inline-flex size-6 items-center justify-center rounded text-muted-foreground hover:bg-muted hover:text-foreground">
          <ArrowUpRight className="size-3.5" />
        </Link>
      </td>
    </tr>
    {job.error && <tr className="border-b last:border-b-0">
      <td colSpan={COLUMNS} className="px-3 pb-2 pt-0">
        <p className={cn("line-clamp-2 rounded-md px-2 py-1 font-mono text-[11px]",
          job.status === "Failed" ? "bg-destructive/10 text-destructive" : "bg-muted text-muted-foreground")}>{job.error}</p>
      </td>
    </tr>}
  </>;
}
