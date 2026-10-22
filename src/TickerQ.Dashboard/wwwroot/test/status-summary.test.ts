import assert from "node:assert/strict";
import test from "node:test";
import {
  aggregateBranchSubtreeStatus,
  isBranchSubtreeSettled,
  summarizeBranchStatuses,
} from "../src/lib/cron/status-config.ts";

test("parallel branch aggregation ignores unused conditional paths", () => {
  assert.equal(
    aggregateBranchSubtreeStatus(["Done", "Skipped", "Skipped"]),
    "Done"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["DueDone", "Skipped"]),
    "DueDone"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["Skipped", "Skipped"]),
    "Skipped"
  );
});

test("parallel branch aggregation preserves actionable subtree states", () => {
  assert.equal(
    aggregateBranchSubtreeStatus(["Done", "Skipped", "Failed"]),
    "Failed"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["Done", "Skipped", "Cancelled"]),
    "Cancelled"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["Done", "Skipped", "InProgress"]),
    "InProgress"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["Skipped", "Queued"]),
    "Queued"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["Skipped", "Idle"]),
    "Idle"
  );
});

test("parallel branch aggregation uses deterministic severity precedence", () => {
  assert.equal(
    aggregateBranchSubtreeStatus([
      "Skipped",
      "DueDone",
      "Done",
      "Idle",
      "Queued",
      "InProgress",
      "Cancelled",
      "Failed",
    ]),
    "InProgress"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["Done", "Failed", "Cancelled"]),
    "Failed"
  );
  assert.equal(
    aggregateBranchSubtreeStatus(["DueDone", "Done"]),
    "Done"
  );
  assert.equal(aggregateBranchSubtreeStatus([]), "Idle");
});

test("parallel branch settlement waits for every persisted descendant", () => {
  assert.equal(isBranchSubtreeSettled(["Done", "Skipped", "Failed"]), true);
  assert.equal(isBranchSubtreeSettled(["Failed", "InProgress"]), false);
  assert.equal(isBranchSubtreeSettled(["Done", "Queued"]), false);
  assert.equal(isBranchSubtreeSettled([]), false);
});

test("parallel join summary groups successful terminal states", () => {
  assert.deepEqual(
    summarizeBranchStatuses(["Done", "DueDone", "Failed"])
      .map(({ key, label, count }) => ({ key, label, count })),
    [
      { key: "done", label: "done", count: 2 },
      { key: "failed", label: "failed", count: 1 },
    ]
  );
});

test("parallel join summary keeps active and waiting states distinct", () => {
  assert.deepEqual(
    summarizeBranchStatuses([
      "InProgress",
      "Queued",
      "Idle",
      "Cancelled",
      "Skipped",
    ]).map(({ key, count }) => ({ key, count })),
    [
      { key: "running", count: 1 },
      { key: "cancelled", count: 1 },
      { key: "skipped", count: 1 },
      { key: "queued", count: 1 },
      { key: "idle", count: 1 },
    ]
  );
});
