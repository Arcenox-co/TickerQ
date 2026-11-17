import test from "node:test";
import assert from "node:assert/strict";

import { dayLabel, mergeAnalytics, offsetMinutes, summarize } from "../src/lib/promise-analytics.ts";
import type { PromiseAnalytics, PromiseDay } from "../src/services/promise-types.ts";

const day = (d: string, patch: Partial<PromiseDay> = {}): PromiseDay => ({
  day: d, done: 0, failed: 0, stopped: 0, pending: 0, fixes: 0, autoFixes: 0, averageMs: null, p95Ms: null, ...patch,
});

const analytics = (id: string, daily: PromiseDay[], patch: Partial<PromiseAnalytics> = {}): PromiseAnalytics => ({
  promiseId: id, fromUtc: "2026-11-13T23:00:00Z", truncated: false, daily,
  sources: { app: 0, fix: 0, autoFix: 0 }, averageMs: null, p95Ms: null, ...patch,
});

test("the offset follows the time zone, daylight saving included", () => {
  assert.equal(offsetMinutes("UTC"), 0);
  assert.equal(offsetMinutes("Europe/Tirane", new Date("2026-01-15T12:00:00Z")), 60);
  assert.equal(offsetMinutes("Europe/Tirane", new Date("2026-07-15T12:00:00Z")), 120);
  assert.equal(offsetMinutes("America/St_Johns", new Date("2026-01-15T12:00:00Z")), -210);
  assert.equal(offsetMinutes("Not/AZone"), 0);
});

test("days read as calendar dates", () => {
  assert.equal(dayLabel("2026-11-15"), "Nov 15");
  assert.equal(dayLabel("2027-01-01"), "Jan 1");
});

test("merged promises add their jobs and keep the slowest p95", () => {
  const merged = mergeAnalytics([
    analytics("a", [day("2026-11-14", { done: 3, failed: 1, averageMs: 100, p95Ms: 200, fixes: 1 })],
      { sources: { app: 3, fix: 1, autoFix: 0 }, averageMs: 100, p95Ms: 200 }),
    analytics("b", [day("2026-11-14", { done: 1, averageMs: 500, p95Ms: 900, autoFixes: 2 })],
      { sources: { app: 0, fix: 0, autoFix: 2 }, averageMs: 500, p95Ms: 900, truncated: true }),
  ])!;
  const [d] = merged.daily;
  assert.deepEqual([d.done, d.failed, d.fixes, d.autoFixes, d.averageMs, d.p95Ms], [4, 1, 1, 2, 180, 900]);
  assert.deepEqual(merged.sources, { app: 3, fix: 1, autoFix: 2 });
  assert.equal(merged.truncated, true);
  assert.equal(mergeAnalytics([]), null);
});

test("the summary finds the busiest day and the worst one", () => {
  const summary = summarize(analytics("a", [
    day("2026-11-13", { done: 2 }),
    day("2026-11-14", { done: 5, failed: 1, fixes: 2 }),
    day("2026-11-15", { done: 1, failed: 2, autoFixes: 1, pending: 1 }),
  ]));
  assert.equal(summary.jobs, 12);
  assert.equal(summary.successRate, 73);
  assert.deepEqual(summary.peak, { day: "2026-11-14", jobs: 6 });
  assert.deepEqual(summary.worst, { day: "2026-11-15", failed: 2 });
  assert.deepEqual([summary.fixes, summary.autoFixes], [2, 1]);
  assert.equal(summarize(analytics("b", [day("2026-11-15")])).peak, null);
});
