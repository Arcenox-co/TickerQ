import test from "node:test";
import assert from "node:assert/strict";

import { checksLate, everyLabel, nextCheckLabel, refreshAfterNextCheck, timeAgo } from "../src/lib/relative-time.ts";

const now = Date.parse("2026-11-15T12:00:00Z");

test("past and future moments read in their largest whole unit", () => {
  assert.equal(timeAgo("2026-11-15T11:59:58Z", now), "just now");
  assert.equal(timeAgo("2026-11-15T11:59:48Z", now), "12s ago");
  assert.equal(timeAgo("2026-11-15T11:55:00Z", now), "5 min ago");
  assert.equal(timeAgo("2026-11-15T09:00:00Z", now), "3 h ago");
  assert.equal(timeAgo("2026-11-13T12:00:00Z", now), "2 d ago");
  assert.equal(timeAgo("2026-09-01T12:00:00Z", now), "2 mo ago");
  assert.equal(timeAgo("2026-11-15T12:00:15Z", now), "in 15s");
  assert.equal(timeAgo("2026-11-15T12:10:00Z", now), "in 10 min");
});

test("server times without an offset are UTC, and missing ones read as a dash", () => {
  assert.equal(timeAgo("2026-11-15T11:55:00", now), "5 min ago");
  assert.equal(timeAgo(null, now), "—");
  assert.equal(timeAgo("not a date", now), "—");
});

test("checks are late once the next one is overdue by more than an interval", () => {
  assert.equal(checksLate("2026-11-15T11:59:50Z", 15, now), false);
  assert.equal(checksLate("2026-11-15T11:59:00Z", 15, now), true);
  assert.equal(checksLate("2026-11-15T11:59:00Z", 300, now), false);
  assert.equal(checksLate(null, 15, now), false);
});

test("the next check reads as a countdown, then due, then late", () => {
  assert.equal(nextCheckLabel("2026-11-15T12:00:12Z", 15, now), "in 12s");
  assert.equal(nextCheckLabel("2026-11-15T11:59:58Z", 15, now), "due now");
  assert.equal(nextCheckLabel("2026-11-15T11:58:00Z", 15, now), "late");
  assert.equal(nextCheckLabel(null, 15, now), "paused");
});

test("the list refreshes just after the soonest next check, within bounds", () => {
  const at = (iso: string | null) => ({ nextCheckAtUtc: iso });
  assert.equal(refreshAfterNextCheck([at("2026-11-15T12:00:10Z"), at("2026-11-15T12:00:20Z")], now), 12_000);
  assert.equal(refreshAfterNextCheck([at("2026-11-15T11:59:00Z")], now), 5_000);
  assert.equal(refreshAfterNextCheck([at("2026-11-15T13:00:00Z")], now), 30_000);
  assert.equal(refreshAfterNextCheck([at(null)], now), 30_000);
});

test("intervals read as people say them", () => {
  assert.deepEqual([everyLabel(15), everyLabel(60), everyLabel(300), everyLabel(7200)],
    ["every 15s", "every 1 min", "every 5 min", "every 2 h"]);
});
