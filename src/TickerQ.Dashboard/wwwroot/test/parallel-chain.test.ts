import assert from "node:assert/strict";
import test from "node:test";
import type { ChainNode } from "../src/lib/cron/chain-node.ts";
import {
  GROUP_CHILD_CONDITIONS,
  parallelChildRole,
  validateParallelGroups,
} from "../src/lib/cron/parallel-chain.ts";

function node(
  functionName: string,
  runCondition: ChainNode["runCondition"] = "OnSuccess",
  children: ChainNode[] = [],
  active = true
): ChainNode {
  return {
    id: functionName,
    functionName,
    runCondition,
    retries: 0,
    retryIntervalsSeconds: "",
    description: "",
    requestJson: "",
    requestValid: true,
    onStale: "Restart",
    deleteBehavior: "Cascade",
    timeoutSeconds: 0,
    active,
    scheduledAt: "",
    children,
  };
}

test("group membership is independent from the visible condition", () => {
  const group = node("$tickerq.parallel-group");
  assert.equal(parallelChildRole(group, node("branch", "InProgress")), "branch");
  assert.equal(parallelChildRole(group, node("success-branch", "GroupOnSuccess")), "branch");
  assert.equal(parallelChildRole(group, node("failure-branch", "GroupOnFailure")), "branch");
  assert.equal(parallelChildRole(group, node("success", "OnSuccess")), "outcome");
  assert.equal(parallelChildRole(node("ordinary"), node("child")), null);
});

test("grouped and final jobs expose their own condition sets", () => {
  assert.deepEqual(GROUP_CHILD_CONDITIONS, [
    "InProgress",
    "OnSuccess",
    "OnFailure",
    "OnCancelled",
    "OnFailureOrCancelled",
    "OnAnyCompletedStatus",
    "OnAnyTerminalStatus",
  ]);

  const group = node("$tickerq.parallel-group");
  const child = node("charge", "GroupOnSuccess");
  assert.equal(parallelChildRole(group, child), "branch");

  child.runCondition = "GroupOnFailure";
  assert.equal(parallelChildRole(group, child), "branch");

  child.runCondition = "OnFailure";
  assert.equal(parallelChildRole(group, child), "outcome");
});

test("parallel groups require a runnable branch but do not require an outcome", () => {
  const emptyGroup = node("$tickerq.parallel-group", "OnSuccess", [
    node("future-branch", "InProgress", [], false),
  ]);
  assert.equal(
    validateParallelGroups(node("root", "OnSuccess", [emptyGroup])),
    "Every group needs at least one grouped job"
  );

  const branchOnly = node("$tickerq.parallel-group", "OnSuccess", [
    node("branch", "InProgress"),
  ]);
  assert.equal(validateParallelGroups(node("root", "OnSuccess", [branchOnly])), null);
});

test("parallel validation walks nested stages", () => {
  const nestedEmpty = node("$tickerq.parallel-group");
  const outer = node("$tickerq.parallel-group", "OnSuccess", [
    node("branch", "InProgress", [nestedEmpty]),
  ]);
  assert.equal(
    validateParallelGroups(node("root", "OnSuccess", [outer])),
    "Every group needs at least one grouped job"
  );
});
