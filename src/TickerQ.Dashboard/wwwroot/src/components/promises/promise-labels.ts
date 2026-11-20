import { CircleSlash, CopyX, Copy, FileExclamationPoint, TimerOff } from "lucide-react";
import type { PromiseFindingKind, PromiseJobStatus } from "@/services/promise-types";
import type { HealthKey, Nouns, PromiseStatus } from "@/lib/promise-list";

import { kindLabels } from "@/lib/promise-list";

export { kindLabels };

/** What a group's guidance can name: the rows, the function, and DoneWhen in words. */
export interface GuideContext {
  words: Nouns;
  fn: string;
  doneWhen: string | null;
}

/** A group's heading, naming the rows where it helps: "Job ran, invoice not updated". */
export function groupTitle(kind: PromiseFindingKind, words: Nouns): string {
  return kind === "DoneButNotApplied" ? `Job ran, ${words.one} not updated` : kindLabels[kind];
}

/** What a group of findings means and what to do about it, said once above its rows. */
export const kindGuides: Record<PromiseFindingKind, (c: GuideContext) => string> = {
  NoJob: ({ words: w, fn }) => `These ${w.many} were saved, but no ${fn} job was ever created for them. Schedule it to process them now.`,
  NeverRan: ({ words: w }) => `These ${w.many} have a job, but it was cancelled, skipped, or is stuck waiting. Start it, or schedule it again.`,
  WillRunTwice: ({ words: w }) => `An extra job is waiting for ${w.many} that are already handled. Cancel it before it runs a second time.`,
  DoneButNotApplied: ({ words: w, fn, doneWhen }) =>
    `The ${fn} job finished without errors, but the ${w.one} in your database still doesn't meet “${doneWhen ?? "its done condition"}”. ` +
    `The job may not have saved its change. Running it again could repeat its work, so check the job's logs, update the ${w.one}, then dismiss.`,
  RanTwice: ({ words: w }) => `The job succeeded more than once for the same ${w.one}, so its work happened twice. Check what the extra run did, then dismiss.`,
};

// Each kind keeps its own icon, so the label and icon carry it and color only supports them.
export const kindIcons: Record<PromiseFindingKind, typeof CircleSlash> = {
  NoJob: CircleSlash,
  NeverRan: TimerOff,
  DoneButNotApplied: FileExclamationPoint,
  WillRunTwice: Copy,
  RanTwice: CopyX,
};

export const kindTones: Record<PromiseFindingKind, string> = {
  NoJob: "bg-amber-500/15 text-amber-800 dark:text-amber-300",
  NeverRan: "bg-destructive/10 text-destructive",
  DoneButNotApplied: "bg-violet-500/15 text-violet-800 dark:text-violet-300",
  WillRunTwice: "bg-sky-500/15 text-sky-800 dark:text-sky-300",
  RanTwice: "bg-rose-500/15 text-rose-800 dark:text-rose-300",
};

export const statusTones: Record<PromiseStatus, string> = {
  NeedsReview: "bg-destructive/10 text-destructive",
  CheckFailed: "bg-amber-500/15 text-amber-800 dark:text-amber-300",
  Paused: "bg-muted text-muted-foreground",
  Done: "bg-emerald-500/15 text-emerald-800 dark:text-emerald-300",
  NotChecked: "bg-muted text-muted-foreground",
};

export const jobStatusLabels: Record<PromiseJobStatus, string> = {
  Idle: "Waiting",
  Queued: "Queued",
  InProgress: "Running",
  Done: "Done",
  DueDone: "Done late",
  Failed: "Failed",
  Cancelled: "Cancelled",
  Skipped: "Skipped",
};

// Validated together for colorblind separation in both themes (see index.css).
export const PROMISE_COLORS: Record<HealthKey, string> = {
  done: "var(--promise-kept)",
  running: "var(--promise-progress)",
  needsReview: "var(--promise-breaking)",
  // A failed job is handled on Executions: gray, told apart from the rest by stripes rather than a new hue.
  failed: "repeating-linear-gradient(135deg, hsl(var(--muted-foreground) / 0.55) 0 3px, transparent 3px 6px)",
  // Dismissed, without a match value, or beyond what one check reads: de-emphasized, not another hue.
  other: "hsl(var(--muted-foreground) / 0.3)",
};

export const HEALTH_LABELS: Record<HealthKey, string> = {
  done: "Done",
  running: "Running",
  needsReview: "Needs review",
  failed: "Failed (see Executions)",
  other: "Dismissed or not checked",
};
