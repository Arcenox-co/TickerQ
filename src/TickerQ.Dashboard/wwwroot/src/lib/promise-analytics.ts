import type { PromiseAnalytics, PromiseDay } from "../services/promise-types";

/** The ranges the charts offer, in days. */
export const RANGES = [7, 14, 30] as const;
export type Range = (typeof RANGES)[number];

/** Minutes ahead of UTC in <paramref name="timeZone"/> at <paramref name="at"/>, so days are bucketed as the viewer sees them. */
export function offsetMinutes(timeZone: string, at: Date = new Date()): number {
  try {
    const name = new Intl.DateTimeFormat("en-US", { timeZone, timeZoneName: "longOffset" })
      .formatToParts(at).find(p => p.type === "timeZoneName")?.value ?? "";
    const match = /GMT([+-])(\d{1,2})(?::(\d{2}))?/.exec(name);
    if (!match) return 0;
    const minutes = Number(match[2]) * 60 + Number(match[3] ?? 0);
    return match[1] === "-" ? -minutes : minutes;
  } catch {
    return 0;
  }
}

/** "Nov 15" for "2026-11-15", read as a calendar date so no time zone shifts it. */
export function dayLabel(day: string): string {
  const [year, month, date] = day.split("-").map(Number);
  return new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" })
    .format(new Date(Date.UTC(year, month - 1, date)));
}

export const dayTotal = (d: PromiseDay) => d.done + d.failed + d.stopped + d.pending;

/**
 * The promises' days added together. Durations can't be added, so the merged days keep the average weighted by jobs
 * that finished and the slowest p95; a single promise keeps its own.
 */
export function mergeAnalytics(list: PromiseAnalytics[]): PromiseAnalytics | null {
  if (list.length === 0) return null;
  if (list.length === 1) return list[0];
  const finished = (d: PromiseDay) => d.done + d.failed;
  const daily = list[0].daily.map((first, i) => {
    const days = list.map(a => a.daily[i]).filter(Boolean);
    const timed = days.filter(d => d.averageMs != null && finished(d) > 0);
    const weight = timed.reduce((sum, d) => sum + finished(d), 0);
    return {
      day: first.day,
      done: days.reduce((sum, d) => sum + d.done, 0),
      failed: days.reduce((sum, d) => sum + d.failed, 0),
      stopped: days.reduce((sum, d) => sum + d.stopped, 0),
      pending: days.reduce((sum, d) => sum + d.pending, 0),
      fixes: days.reduce((sum, d) => sum + d.fixes, 0),
      autoFixes: days.reduce((sum, d) => sum + d.autoFixes, 0),
      averageMs: weight === 0 ? null : timed.reduce((sum, d) => sum + d.averageMs! * finished(d), 0) / weight,
      p95Ms: timed.length === 0 ? null : Math.max(...timed.map(d => d.p95Ms ?? 0)),
    };
  });
  const timed = list.filter(a => a.averageMs != null);
  return {
    promiseId: "all",
    fromUtc: list[0].fromUtc,
    truncated: list.some(a => a.truncated),
    daily,
    sources: {
      app: list.reduce((sum, a) => sum + a.sources.app, 0),
      fix: list.reduce((sum, a) => sum + a.sources.fix, 0),
      autoFix: list.reduce((sum, a) => sum + a.sources.autoFix, 0),
    },
    averageMs: timed.length === 0 ? null : timed.reduce((sum, a) => sum + a.averageMs!, 0) / timed.length,
    p95Ms: timed.length === 0 ? null : Math.max(...timed.map(a => a.p95Ms ?? 0)),
  };
}

export interface RangeSummary {
  jobs: number;
  done: number;
  failed: number;
  /** Done among finished jobs, as a whole percent; null before any finished. */
  successRate: number | null;
  fixes: number;
  autoFixes: number;
  /** The busiest day, or null when there were no jobs. */
  peak: { day: string; jobs: number } | null;
  /** The day with the most failures, or null when nothing failed. */
  worst: { day: string; failed: number } | null;
}

export function summarize(analytics: PromiseAnalytics): RangeSummary {
  const sum = (pick: (d: PromiseDay) => number) => analytics.daily.reduce((total, d) => total + pick(d), 0);
  const done = sum(d => d.done);
  const failed = sum(d => d.failed);
  const peak = analytics.daily.reduce<PromiseDay | null>((best, d) => dayTotal(d) > (best ? dayTotal(best) : 0) ? d : best, null);
  const worst = analytics.daily.reduce<PromiseDay | null>((best, d) => d.failed > (best?.failed ?? 0) ? d : best, null);
  return {
    jobs: sum(dayTotal),
    done,
    failed,
    successRate: done + failed === 0 ? null : Math.round((done / (done + failed)) * 100),
    fixes: sum(d => d.fixes),
    autoFixes: sum(d => d.autoFixes),
    peak: peak ? { day: peak.day, jobs: dayTotal(peak) } : null,
    worst: worst ? { day: worst.day, failed: worst.failed } : null,
  };
}
