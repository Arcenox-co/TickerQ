import type { PromiseJob, PromiseJobList } from "../services/promise-types";

/** The job list's filters: everything, failures, jobs that did not run, and jobs a promise fix created. */
export type JobFilter = "all" | "failed" | "notRun" | "fixes";

export const JOB_FILTERS: JobFilter[] = ["all", "failed", "notRun", "fixes"];

export const jobFilterLabels: Record<JobFilter, string> = {
  all: "All",
  failed: "Failed",
  notRun: "Not run",
  fixes: "From fixes",
};

const NOT_RUN = new Set(["Idle", "Queued", "Cancelled", "Skipped"]);

function matches(job: PromiseJob, filter: JobFilter): boolean {
  switch (filter) {
    case "failed": return job.status === "Failed";
    case "notRun": return NOT_RUN.has(job.status);
    case "fixes": return job.source !== "App";
    default: return true;
  }
}

/** Jobs passing the filter whose key, label, id or fixer contains the search, case-insensitively. */
export function filterJobs(jobs: PromiseJob[], filter: JobFilter, search: string): PromiseJob[] {
  const needle = search.trim().toLowerCase();
  return jobs.filter(job => matches(job, filter) && (!needle ||
    [job.key, job.label, job.id, job.fixedBy].some(value => value?.toLowerCase().includes(needle))));
}

export function jobFilterCount(jobs: PromiseJob[], filter: JobFilter): number {
  return jobs.filter(job => matches(job, filter)).length;
}

/** Who scheduled the job, as the table says it. */
export function jobSourceLabel(job: PromiseJob): string {
  switch (job.source) {
    case "Fix": return job.fixedBy ? `Fix by ${job.fixedBy}` : "Fix";
    case "AutoFix": return "Auto-fix";
    default: return "App";
  }
}

/** Jobs in the window: the totals' sum, which can exceed the listed jobs. */
export function windowTotal(list: PromiseJobList): number {
  const t = list.totals;
  return t.done + t.failed + t.running + t.waiting + t.stopped;
}

/** Done among the jobs that finished (done or failed), as a whole percent; null before any finished. */
export function successRate(list: PromiseJobList): number | null {
  const finished = list.totals.done + list.totals.failed;
  return finished === 0 ? null : Math.round((list.totals.done / finished) * 100);
}

const RAN = new Set(["InProgress", "Done", "DueDone", "Failed"]);

/** Whether the job started; a cancelled or skipped job also gets an executed time, but never ran. */
export function hasRun(job: PromiseJob): boolean {
  return RAN.has(job.status) && job.executedAtUtc != null;
}

/** The average duration of the listed jobs that ran, in milliseconds; null when none ran. */
export function averageDuration(jobs: PromiseJob[]): number | null {
  const ran = jobs.filter(job => hasRun(job) && job.elapsedMs > 0);
  return ran.length === 0 ? null : ran.reduce((sum, job) => sum + job.elapsedMs, 0) / ran.length;
}
