// The promise API under /api/dashboard/promises. Promises are declared in code; the dashboard watches them, fixes or
// dismisses the rows whose job was missed, and tunes a few settings per promise.

export type PromiseJobStatus = "Idle" | "Queued" | "InProgress" | "Done" | "DueDone" | "Failed" | "Cancelled" | "Skipped";

/**
 * Why a row needs review: its work was missed (the first three), or it ran or will run twice. A failed job is not one:
 * Executions, retries and alerts own failures.
 */
export type PromiseFindingKind = "NoJob" | "NeverRan" | "DoneButNotApplied" | "RanTwice" | "WillRunTwice";

/** What the fix does: schedule a new job, run the waiting job now, cancel the extra waiting jobs, or nothing. */
export type PromiseFix = "None" | "NewJob" | "RunNow" | "CancelExtra";

export type PromiseActivityKind = "fix" | "fix_all" | "auto_fix" | "auto_fix_paused" | "dismiss" | "settings";

export interface PromiseAutoFix {
  enabled: boolean;
  /** Whether code turned automatic fixing on with AutoFix(). */
  fromCode: boolean;
  maxPerCheck: number;
  paused: boolean;
  pausedReason: string | null;
}

export interface PromiseSettings {
  paused: boolean;
  /** The dashboard's time limit, or null when the code's applies. */
  withinSeconds: number | null;
  withinSecondsFromCode: number;
}

export interface PromiseCounts {
  /** Rows in the window whose time limit has passed. */
  watched: number;
  done: number;
  running: number;
  /** Rows whose latest job failed. They are not findings. */
  failed: number;
  needsReview: number;
  /** Of needsReview, the rows that ran or will run twice; the rest were missed. */
  duplicates: number;
  dismissed: number;
  /** Rows with an empty match value, which no job can match. */
  unmatchable: number;
}

export interface PromiseLastJob {
  id: string;
  status: PromiseJobStatus;
  error: string | null;
}

export interface PromiseDismissed {
  by: string;
  at: string;
  reason: string | null;
}

export interface PromiseFinding {
  id: string;
  kind: PromiseFindingKind;
  /** The row's match values, for example "acme · 1042". */
  key: string;
  label: string;
  reason: string;
  lastJob: PromiseLastJob | null;
  fix: PromiseFix;
  detectedAtUtc: string;
  dismissed: PromiseDismissed | null;
}

export interface PromiseActivity {
  atUtc: string;
  kind: PromiseActivityKind;
  text: string;
}

export interface PromiseSnapshot {
  id: string;
  name: string;
  function: string;
  rowType: string;
  summary: string;
  /** When the promise's state was first stored; null before its first check. The list is ordered by it, newest first. */
  createdAtUtc: string | null;
  /** DoneWhen in words, such as "Status is Sent"; null when the promise has none. */
  doneWhen: string | null;
  /** How the promise is set up in code, with the options its checks run under. */
  rule: PromiseRule;
  /** False when the code allows a row to run more than once (AllowRepeats). */
  checksDuplicates: boolean;
  source: "Code";
  autoFix: PromiseAutoFix;
  settings: PromiseSettings;
  checkedAtUtc: string | null;
  /** When the next scheduled check is due; null while paused. */
  nextCheckAtUtc: string | null;
  checkIntervalSeconds: number;
  durationMs: number | null;
  error: string | null;
  truncated: boolean;
  windowStartUtc: string | null;
  counts: PromiseCounts;
  /** At most 200, newest row first. */
  findings: PromiseFinding[];
  /** At most 50, newest first. */
  activity: PromiseActivity[];
  canManage: boolean;
}

/** Who scheduled a job: the application, a person's fix, or automatic fixing. */
export type PromiseJobSource = "App" | "Fix" | "AutoFix";

export interface PromiseJob {
  id: string;
  status: PromiseJobStatus;
  /** The job's match values, as findings show them; null when its request has none. */
  key: string | null;
  /** The row's label for a fix job, otherwise the job's description. */
  label: string | null;
  source: PromiseJobSource;
  /** Who ran the fix, for source "Fix". */
  fixedBy: string | null;
  createdAtUtc: string;
  executionTimeUtc: string | null;
  executedAtUtc: string | null;
  elapsedMs: number;
  retryCount: number;
  retries: number;
  /** The failure's message, or why it was skipped. */
  error: string | null;
}

/** GET /promises/{id}/jobs: the function's newest jobs in the promise's window, with totals for the whole window. */
export interface PromiseJobList {
  windowStartUtc: string;
  totals: { done: number; failed: number; running: number; waiting: number; stopped: number };
  /** Newest first, at most 100. */
  jobs: PromiseJob[];
}

/** One day of a promise's jobs, in the viewer's time zone. */
export interface PromiseDay {
  /** yyyy-MM-dd */
  day: string;
  done: number;
  failed: number;
  /** Cancelled or skipped. */
  stopped: number;
  /** Waiting or running. */
  pending: number;
  fixes: number;
  autoFixes: number;
  averageMs: number | null;
  p95Ms: number | null;
}

/** GET /promises/analytics and /promises/{id}/analytics. */
export interface PromiseAnalytics {
  promiseId: string;
  fromUtc: string;
  truncated: boolean;
  /** Oldest first, one per day. */
  daily: PromiseDay[];
  sources: { app: number; fix: number; autoFix: number };
  averageMs: number | null;
  p95Ms: number | null;
}

/**
 * A promise as declared in code, call by call. The rows query and the job's request are compiled code, so they are
 * described by their types rather than shown.
 */
export interface PromiseRule {
  /** Where it was declared: services.MapTicker<…>().Promise(…), or options.ConfigurePromises(p => p.For(…)). */
  declaredOn: "MapTicker" | "ConfigurePromises";
  rowType: string;
  /** The service the rows are read from, such as "BillingDbContext"; null when read from the service provider. */
  rowsFrom: string | null;
  /** The class registered with MapTicker; null for a function referenced by name. */
  functionClass: string | null;
  requestType: string;
  matchBy: string[];
  withinSeconds: number;
  after: string | null;
  /** DoneWhen in words; null when a row is done once one of its jobs succeeds. */
  doneWhen: string | null;
  hasLabel: boolean;
  /** The name given with Named(); null when the promise uses its default name. */
  namedInCode: string | null;
  /** AutoFix()'s limit per check; null when fixing is left to a person. */
  autoFixMax: number | null;
  allowRepeats: boolean;
  checks: {
    checkIntervalSeconds: number;
    neverRanAfterSeconds: number;
    maxRowsPerCheck: number;
    maxJobsPerCheck: number;
    lookBackDays: number;
    lookBackFromRetention: boolean;
  };
}

/** PUT /promises/{id}/settings. An absent property is unchanged; null goes back to the code's value. */
export interface PromiseSettingsChange {
  paused?: boolean;
  withinSeconds?: number | null;
  /** true also resumes a paused automatic fixing. */
  autoFix?: boolean | null;
}

export interface PromiseFixResult {
  jobId: string;
  fix: "NewJob" | "RunNow" | "CancelExtra";
  promise: PromiseSnapshot;
}

export interface PromiseFixAllResult {
  fixed: number;
  failed: number;
  remaining: number;
  promise: PromiseSnapshot;
}
