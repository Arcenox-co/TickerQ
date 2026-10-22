import assert from "node:assert/strict";
import test from "node:test";
import {
  computeRetryImpact,
  type RetryImpactNode,
} from "../src/lib/cron/retry-impact.ts";

function node(
  id: string,
  runCondition: string | null = "OnSuccess",
  children: RetryImpactNode[] = []
): RetryImpactNode {
  return { id, functionName: id, runCondition, children };
}

function group(id: string, children: RetryImpactNode[]): RetryImpactNode {
  return { id, functionName: "$tickerq.parallel-group", runCondition: "OnSuccess", children };
}

test("ordinary retry reports the selected job and its downstream subtree", () => {
  const tree = node("root", null, [node("selected", "OnSuccess", [node("child"), node("child-2")])]);
  const impact = computeRetryImpact(tree, "selected");

  assert.deepEqual(impact?.affectedJobIds.sort(), ["child", "child-2", "selected"]);
  assert.equal(impact?.downstreamJobCount, 2);
  assert.equal(impact?.additionalOutcomeJobCount, 0);
  assert.equal(impact?.recalculatedGroupCount, 0);
});

test("retrying a grouped branch recalculates outcomes but reuses sibling branches", () => {
  const tree = node("root", null, [group("group", [
    node("selected", "GroupOnSuccess", [node("selected-child")]),
    node("settled-sibling", "InProgress"),
    node("on-success", "OnSuccess", [node("outcome-child")]),
    node("on-failure", "OnFailure"),
  ])]);
  const impact = computeRetryImpact(tree, "selected");

  assert.deepEqual(impact?.affectedJobIds.sort(), [
    "on-failure",
    "on-success",
    "outcome-child",
    "selected",
    "selected-child",
  ]);
  assert.equal(impact?.downstreamJobCount, 1);
  assert.equal(impact?.additionalOutcomeJobCount, 3);
  assert.equal(impact?.recalculatedGroupCount, 1);
  assert.equal(impact?.reusedGroupedBranchCount, 1);
});

test("retrying a group reruns every grouped branch and outcome", () => {
  const tree = node("root", null, [group("group", [
    node("branch-a", "InProgress", [node("branch-a-child")]),
    node("branch-b", "GroupOnSuccess"),
    node("on-success", "OnSuccess", [node("outcome-child")]),
    node("on-failure", "OnFailure"),
  ])]);
  const impact = computeRetryImpact(tree, "group");

  assert.deepEqual(impact?.affectedJobIds.sort(), [
    "branch-a",
    "branch-a-child",
    "branch-b",
    "on-failure",
    "on-success",
    "outcome-child",
  ]);
  assert.equal(impact?.downstreamJobCount, 6);
  assert.equal(impact?.additionalOutcomeJobCount, 0);
  assert.equal(impact?.recalculatedGroupCount, 1);
  assert.equal(impact?.reusedGroupedBranchCount, 0);
});

test("retrying a group outcome does not replay sibling outcomes", () => {
  const tree = node("root", null, [group("group", [
    node("branch", "InProgress"),
    node("selected-outcome", "OnFailure", [node("selected-child")]),
    node("other-outcome", "OnSuccess"),
  ])]);
  const impact = computeRetryImpact(tree, "selected-outcome");

  assert.deepEqual(impact?.affectedJobIds.sort(), ["selected-child", "selected-outcome"]);
  assert.equal(impact?.downstreamJobCount, 1);
  assert.equal(impact?.additionalOutcomeJobCount, 0);
  assert.equal(impact?.recalculatedGroupCount, 0);
  assert.equal(impact?.reusedGroupedBranchCount, 0);
});
