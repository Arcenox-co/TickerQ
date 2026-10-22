import assert from "node:assert/strict";
import test from "node:test";
import {
  RUN_CONDITION_CONFIG,
  baseRunCondition,
  conditionEvaluationState,
  groupedRunCondition,
  isGroupedRunCondition,
  runConditionHsl,
} from "../src/lib/cron/run-condition.ts";

test("parallel exposes its stored rule and reuses the running status color", () => {
  const condition = RUN_CONDITION_CONFIG.InProgress;

  assert.equal(condition.label, "Parallel");
  assert.equal(condition.cssVar, "var(--status-warning)");
  assert.match(condition.description, /parent enters InProgress/);
  assert.equal(runConditionHsl("InProgress"), "hsl(var(--status-warning))");
});

test("group conditions preserve membership while exposing the normal rule", () => {
  assert.equal(groupedRunCondition("OnSuccess"), "GroupOnSuccess");
  assert.equal(groupedRunCondition("OnFailure"), "GroupOnFailure");
  assert.equal(baseRunCondition("GroupOnSuccess"), "OnSuccess");
  assert.equal(baseRunCondition("GroupOnFailure"), "OnFailure");
  assert.equal(isGroupedRunCondition("GroupOnSuccess"), true);
  assert.equal(isGroupedRunCondition("OnSuccess"), false);
  assert.equal(runConditionHsl("GroupOnSuccess"), "hsl(var(--status-healthy))");
});

test("all-terminal continuation is described as a settled join with its own color", () => {
  const condition = RUN_CONDITION_CONFIG.OnAnyTerminalStatus;

  assert.equal(condition.label, "After all settle");
  assert.equal(condition.cssVar, "var(--condition-finally)");
  assert.match(condition.description, /Skipped/);
  assert.equal(
    runConditionHsl("OnAnyTerminalStatus"),
    "hsl(var(--condition-finally))"
  );
});

test("condition evaluation is separate from the job result", () => {
  assert.equal(conditionEvaluationState("Idle"), "waiting");
  assert.equal(conditionEvaluationState("Queued"), "waiting");
  assert.equal(conditionEvaluationState("Skipped"), "not-met");
  assert.equal(conditionEvaluationState("InProgress"), "met");
  assert.equal(conditionEvaluationState("Done"), "met");
  assert.equal(conditionEvaluationState("Failed"), "met");
  assert.equal(conditionEvaluationState("Cancelled"), "not-evaluated");
  assert.equal(conditionEvaluationState("Cancelled", true), "met");
});
