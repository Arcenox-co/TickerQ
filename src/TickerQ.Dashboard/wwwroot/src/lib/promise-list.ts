import type { PromiseCounts, PromiseFinding, PromiseFindingKind, PromiseSnapshot } from "../services/promise-types";
import { parseUtc } from "./relative-time.ts";

export type PromiseStatus = "CheckFailed" | "NeedsReview" | "NotChecked" | "Paused" | "Done";

export const statusLabels: Record<PromiseStatus, string> = {
  CheckFailed: "Check failed",
  NeedsReview: "Needs review",
  NotChecked: "Not checked yet",
  Paused: "Disabled",
  Done: "Done",
};

/**
 * One status per promise. A disabled (paused) promise is not checked, so its old result is not its status; a failed check may
 * hide findings, so it outranks them.
 */
export function promiseStatus(promise: PromiseSnapshot): PromiseStatus {
  return promise.settings.paused ? "Paused" : resultStatus(promise);
}

/** What the last check found, whether or not the promise is enabled, for where its state is shown on its own. */
export function resultStatus(promise: PromiseSnapshot): Exclude<PromiseStatus, "Paused"> {
  if (promise.error) return "CheckFailed";
  if (!promise.checkedAtUtc) return "NotChecked";
  return promise.counts.needsReview > 0 ? "NeedsReview" : "Done";
}

export type PromiseFilter = "all" | PromiseStatus;

/**
 * Search by name, job, row type or summary and filter by status. Ordered by when each promise was created, newest
 * first, never by what changes, so rows keep their place as checks update them. A promise not yet stored is newest.
 */
/**
 * Whether a promise is under a filter: "Disabled" (Paused) by its state, the rest by what its last check found, so a
 * disabled promise with rows to review still counts under "Needs review".
 */
export function matchesFilter(promise: PromiseSnapshot, filter: PromiseFilter): boolean {
  if (filter === "all") return true;
  return filter === "Paused" ? promise.settings.paused : resultStatus(promise) === filter;
}

export function filterPromises(promises: PromiseSnapshot[], search: string, filter: PromiseFilter): PromiseSnapshot[] {
  const term = search.trim().toLowerCase();
  return promises
    .filter(p => matchesFilter(p, filter))
    .filter(p => !term || [p.name, p.function, p.rowType, p.summary].some(v => v?.toLowerCase().includes(term)))
    .sort((a, b) => created(b) - created(a) || a.name.localeCompare(b.name));
}

function created(promise: PromiseSnapshot): number {
  return parseUtc(promise.createdAtUtc) ?? Number.MAX_SAFE_INTEGER;
}

export interface Page<T> {
  items: T[];
  page: number;
  pages: number;
  from: number;
  to: number;
  total: number;
}

/** A page of items, with the page number clamped so a shrinking list never shows an empty page. */
export function paginate<T>(items: T[], page: number, size: number): Page<T> {
  const pages = Math.max(1, Math.ceil(items.length / size));
  const current = Math.min(Math.max(1, page), pages);
  const start = (current - 1) * size;
  return {
    items: items.slice(start, start + size),
    page: current,
    pages,
    from: items.length === 0 ? 0 : start + 1,
    to: Math.min(start + size, items.length),
    total: items.length,
  };
}

export interface Nouns {
  one: string;
  many: string;
}

export const ROWS: Nouns = { one: "row", many: "rows" };

/** Everyday words for a row type, so a promise speaks of "orders" rather than "rows": OrderLine → order line / order lines. */
export function nouns(rowType: string | null | undefined): Nouns {
  const name = rowType?.split(/[.+]/).pop()?.replace(/[`<].*$/, "").trim();
  if (!name) return ROWS;
  const one = name.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z])([A-Z][a-z])/g, "$1 $2").toLowerCase();
  if (/[^aeiou]y$/.test(one)) return { one, many: `${one.slice(0, -1)}ies` };
  if (/(s|x|z|ch|sh)$/.test(one)) return { one, many: `${one}es` };
  return { one, many: `${one}s` };
}

/** "1 order", "3 orders". */
export function countOf(n: number, words: Nouns): string {
  return `${n.toLocaleString()} ${n === 1 ? words.one : words.many}`;
}

export type HealthKey = "done" | "running" | "needsReview" | "failed" | "other";

export interface HealthPart {
  key: HealthKey;
  value: number;
}

/**
 * The watched rows split into done, running, needs review, failed and the rest (dismissed, without a match value, or
 * beyond what one check reads). The parts always add up to watched.
 */
export function healthParts(counts: PromiseCounts): HealthPart[] {
  let left = Math.max(0, counts.watched);
  const take = (value: number) => {
    const part = Math.min(Math.max(0, value), left);
    left -= part;
    return part;
  };
  const parts: HealthPart[] = [
    { key: "done", value: take(counts.done) },
    { key: "running", value: take(counts.running) },
    // Duplicates are about jobs, and their rows may be done, so only missed rows are a part of the watched rows.
    { key: "needsReview", value: take(counts.needsReview - (counts.duplicates ?? 0)) },
    { key: "failed", value: take(counts.failed) },
  ];
  parts.push({ key: "other", value: left });
  return parts;
}

const ZERO: PromiseCounts = { watched: 0, done: 0, running: 0, failed: 0, needsReview: 0, duplicates: 0, dismissed: 0, unmatchable: 0 };

export function totalCounts(promises: PromiseSnapshot[]): PromiseCounts {
  return promises.reduce<PromiseCounts>((sum, p) => ({
    watched: sum.watched + p.counts.watched,
    done: sum.done + p.counts.done,
    running: sum.running + p.counts.running,
    failed: sum.failed + p.counts.failed,
    needsReview: sum.needsReview + p.counts.needsReview,
    duplicates: sum.duplicates + (p.counts.duplicates ?? 0),
    dismissed: sum.dismissed + p.counts.dismissed,
    unmatchable: sum.unmatchable + p.counts.unmatchable,
  }), ZERO);
}

/** Share of watched rows done, as a whole percent; null when nothing is watched yet. */
export function donePercent(counts: Pick<PromiseCounts, "watched" | "done">): number | null {
  if (counts.watched <= 0) return null;
  return Math.round((Math.min(counts.done, counts.watched) / counts.watched) * 100);
}

export function donePercentText(counts: Pick<PromiseCounts, "watched" | "done">): string {
  const percent = donePercent(counts);
  return percent == null ? "–" : `${percent}%`;
}

export const KINDS: PromiseFindingKind[] = ["NoJob", "NeverRan", "DoneButNotApplied", "WillRunTwice", "RanTwice"];

export const kindLabels: Record<PromiseFindingKind, string> = {
  NoJob: "Never scheduled",
  NeverRan: "Never ran",
  DoneButNotApplied: "Job ran, row not updated",
  WillRunTwice: "Will run twice",
  RanTwice: "Ran twice",
};

/** Kinds where the row ran, or will run, more than once. */
export const isDuplicateKind = (kind: PromiseFindingKind) => kind === "RanTwice" || kind === "WillRunTwice";

export const isOpen = (finding: PromiseFinding) => finding.dismissed == null;

/** Rows needing review across promises, by why. Counts the findings each promise lists. */
export function missedByKind(promises: PromiseSnapshot[]): { kind: PromiseFindingKind; count: number }[] {
  const open = promises.flatMap(p => p.findings.filter(isOpen));
  return KINDS.map(kind => ({ kind, count: open.filter(f => f.kind === kind).length }));
}

export type FindingTab = "open" | "dismissed";

export function findingsFor(findings: PromiseFinding[], tab: FindingTab, kind: PromiseFindingKind | null): PromiseFinding[] {
  return findings.filter(f => (tab === "open") === isOpen(f) && (kind == null || f.kind === kind));
}

/** Open findings "Run fix for all" can fix. */
export function fixable(findings: PromiseFinding[]): PromiseFinding[] {
  return findings.filter(f => isOpen(f) && f.fix !== "None");
}

/** The most findings one "Run fix for all" fixes; the rest wait for the next run. */
export const FIX_ALL_LIMIT = 100;

/** The button for a finding, saying what it does, or null when it can only be reported. */
export function fixLabel(finding: PromiseFinding): string | null {
  switch (finding.fix) {
    case "NewJob": return finding.kind === "NoJob" ? "Schedule job" : "Schedule again";
    case "RunNow": return "Start now";
    case "CancelExtra": return "Cancel extra job";
    default: return null;
  }
}

/**
 * Why a row has no button when its kind usually has one: a waiting job that is a chain step, or a node that doesn't
 * run jobs. Kinds that are only ever reported say why in their group instead.
 */
export function noFixReason(finding: PromiseFinding): string | null {
  return finding.fix === "None" && finding.kind === "NeverRan"
    ? "Can't be started from here — it's a chain step, or this node doesn't run jobs. Open the job to look at it."
    : null;
}

/** The order findings are grouped in: what a button fixes first, what needs a person's look last. */
export const REVIEW_ORDER: PromiseFindingKind[] = ["NoJob", "NeverRan", "WillRunTwice", "DoneButNotApplied", "RanTwice"];

/** Kinds no button fixes: the work already happened, so a person looks and dismisses. */
export const isReportOnly = (kind: PromiseFindingKind) => kind === "DoneButNotApplied" || kind === "RanTwice";

/** Findings by kind, in review order, leaving out empty kinds; each group keeps the newest row first. */
export function groupFindings(findings: PromiseFinding[]): { kind: PromiseFindingKind; findings: PromiseFinding[] }[] {
  return REVIEW_ORDER
    .map(kind => ({ kind, findings: findings.filter(f => f.kind === kind) }))
    .filter(group => group.findings.length > 0);
}
