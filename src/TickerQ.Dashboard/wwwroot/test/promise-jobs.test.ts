import test from "node:test";
import assert from "node:assert/strict";

import { averageDuration, filterJobs, jobFilterCount, jobSourceLabel, successRate, windowTotal } from "../src/lib/promise-jobs.ts";
import type { PromiseJob, PromiseJobList } from "../src/services/promise-types.ts";

const job = (patch: Partial<PromiseJob> = {}): PromiseJob => ({
  id: "4f1c0000-0000-0000-0000-000000000000", status: "Done", key: "ORD-1", label: null, source: "App", fixedBy: null,
  createdAtUtc: "2026-11-15T10:00:00Z", executionTimeUtc: "2026-11-15T10:00:00Z", executedAtUtc: "2026-11-15T10:00:01Z",
  elapsedMs: 100, retryCount: 0, retries: 0, error: null, ...patch,
});

const list = (totals: Partial<PromiseJobList["totals"]>): PromiseJobList => ({
  windowStartUtc: "2026-10-16T10:00:00Z", jobs: [],
  totals: { done: 0, failed: 0, running: 0, waiting: 0, stopped: 0, ...totals },
});

test("filters split failures, jobs that never ran, and jobs a fix created", () => {
  const jobs = [
    job({ key: "ORD-1" }),
    job({ key: "ORD-2", status: "Failed", error: "SMTP refused" }),
    job({ key: "ORD-3", status: "Cancelled" }),
    job({ key: "ORD-4", status: "Idle", source: "Fix", fixedBy: "ann" }),
    job({ key: "ORD-5", source: "AutoFix" }),
  ];
  assert.deepEqual(filterJobs(jobs, "failed", "").map(j => j.key), ["ORD-2"]);
  assert.deepEqual(filterJobs(jobs, "notRun", "").map(j => j.key), ["ORD-3", "ORD-4"]);
  assert.deepEqual(filterJobs(jobs, "fixes", "").map(j => j.key), ["ORD-4", "ORD-5"]);
  assert.equal(jobFilterCount(jobs, "all"), 5);
});

test("search matches the key, the job id or who fixed it", () => {
  const jobs = [job({ key: "ORD-1" }), job({ key: "ORD-2", source: "Fix", fixedBy: "Ann" }), job({ key: null, id: "abcd-1" })];
  assert.deepEqual(filterJobs(jobs, "all", " ord-1 ").map(j => j.key), ["ORD-1"]);
  assert.deepEqual(filterJobs(jobs, "all", "ann").map(j => j.key), ["ORD-2"]);
  assert.deepEqual(filterJobs(jobs, "all", "ABCD").map(j => j.id), ["abcd-1"]);
  assert.deepEqual(filterJobs([job({ key: "7", label: "INV-1007 for Initech" })], "all", "initech").map(j => j.key), ["7"]);
});

test("the source says who scheduled the job", () => {
  assert.equal(jobSourceLabel(job()), "App");
  assert.equal(jobSourceLabel(job({ source: "Fix", fixedBy: "ann" })), "Fix by ann");
  assert.equal(jobSourceLabel(job({ source: "Fix" })), "Fix");
  assert.equal(jobSourceLabel(job({ source: "AutoFix" })), "Auto-fix");
});

test("totals give the window's size and success rate", () => {
  assert.equal(windowTotal(list({ done: 8, failed: 2, waiting: 1 })), 11);
  assert.equal(successRate(list({ done: 8, failed: 2, waiting: 1 })), 80);
  assert.equal(successRate(list({ waiting: 3 })), null);
});

test("the average duration counts only jobs that ran", () => {
  assert.equal(averageDuration([job({ elapsedMs: 100 }), job({ status: "Cancelled", elapsedMs: 900 })]), 100);
  assert.equal(averageDuration([job({ elapsedMs: 100 }), job({ elapsedMs: 300 }), job({ executedAtUtc: null, elapsedMs: 0 })]), 200);
  assert.equal(averageDuration([job({ executedAtUtc: null, elapsedMs: 0 })]), null);
});
