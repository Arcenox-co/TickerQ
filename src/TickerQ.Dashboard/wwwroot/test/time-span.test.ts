import { test } from "node:test";
import assert from "node:assert/strict";
import { formatMinimumSchedulerWait, formatTimeSpan } from "../src/lib/time-span.ts";

test("retention windows retain day and sub-day precision", () => {
  assert.equal(formatTimeSpan("30.00:00:00"), "30 days");
  assert.equal(formatTimeSpan("1.02:03:04.5000000"), "1 day, 2 hours, 3 minutes, 4.5 seconds");
  assert.equal(formatTimeSpan("01:00:00"), "1 hour");
  assert.equal(formatTimeSpan("00:01:00"), "1 minute");
});

test("scheduler durations preserve fractional seconds", () => {
  assert.equal(formatTimeSpan("00:00:00.5000000"), "500 ms");
  assert.equal(formatTimeSpan("00:00:00.0000001"), "0.0001 ms");
  assert.equal(formatTimeSpan("00:00:01"), "1 second");
  assert.equal(formatTimeSpan("00:00:00"), "0 seconds");
});

test("minimum scheduler wait reflects the runtime's zero floor without hiding invalid values", () => {
  assert.equal(formatMinimumSchedulerWait("-00:00:01"), "0 seconds");
  assert.equal(formatMinimumSchedulerWait("00:00:00"), "0 seconds");
  assert.equal(formatMinimumSchedulerWait("00:00:00.2500000"), "250 ms");
  assert.equal(formatMinimumSchedulerWait("00:00:01"), "1 second");
  for (const value of [null, undefined, "-invalid", "--00:00:01"])
    assert.equal(formatMinimumSchedulerWait(value), "Unavailable");
});

test("configured durations can show signed values without implying an effective timeout", () => {
  assert.equal(formatTimeSpan("-00:00:01", "Unavailable", true), "−1 second");
  assert.equal(formatTimeSpan("-00:00:00", "Unavailable", true), "0 seconds");
  assert.equal(formatTimeSpan("-bad", "Unavailable", true), "Unavailable");
});

test("missing and invalid settings do not masquerade as zero or disabled", () => {
  assert.equal(formatTimeSpan(null, "No default timeout"), "No default timeout");
  assert.equal(formatTimeSpan(undefined), "Unavailable");
  for (const value of ["", "30 days", "-1.00:00:00", "24:00:00", "00:60:00", "00:00:60"])
    assert.equal(formatTimeSpan(value), "Unavailable");
});
