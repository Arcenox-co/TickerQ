import assert from "node:assert/strict";
import test from "node:test";
import {
  computeChainFlowLayout,
  mergePortX,
} from "../src/lib/cron/chain-flow-layout.ts";

type Node = {
  id: string;
  functionName: string;
  runCondition?: string;
  children: Node[];
};

const leaf = (id: string, runCondition = "OnSuccess"): Node => ({
  id,
  functionName: id,
  runCondition,
  children: [],
});

test("parallel continuations render after the deepest branch behind one merge rail", () => {
  const branchA = leaf("branch-a", "GroupOnSuccess");
  branchA.children.push(leaf("branch-a-audit"));
  const root: Node = {
    id: "root",
    functionName: "Prepare",
    children: [{
      id: "group",
      functionName: "$tickerq.parallel-group",
      runCondition: "OnSuccess",
      children: [
        branchA,
        leaf("branch-b", "GroupOnFailure"),
        leaf("success-join", "OnSuccess"),
        leaf("failure-join", "OnFailure"),
        leaf("finally-join", "OnAnyTerminalStatus"),
      ],
    }],
  };

  const layout = computeChainFlowLayout(root, {
    nodeWidth: 220,
    horizontalGap: 32,
    verticalGap: 56,
    nodeHeight: () => 72,
  });
  const byId = new Map(layout.positions.map(position => [position.id, position]));

  assert(byId.get("branch-a")!.y < byId.get("branch-a-audit")!.y);
  assert(byId.get("branch-a-audit")!.y < byId.get("success-join")!.y);
  assert.equal(byId.get("success-join")!.y, byId.get("failure-join")!.y);
  assert.equal(byId.get("failure-join")!.y, byId.get("finally-join")!.y);
  assert.deepEqual(layout.edges.filter(edge => edge.fromId === "group")
    .map(edge => edge.toId).sort(), ["branch-a", "branch-b"]);
  assert.equal(layout.merges.length, 1);
  assert.equal(layout.merges[0].groupId, "group");
  assert.deepEqual(layout.merges[0].sourceIds.sort(), ["branch-a-audit", "branch-b"]);
  assert.deepEqual(layout.merges[0].targetIds.sort(), ["failure-join", "finally-join", "success-join"]);
  assert.equal(layout.merges[0].anchorX, layout.width / 2);
  assert.equal(layout.stages.length, 1);
  assert.equal(layout.stages[0].groupId, "group");
  assert.deepEqual(layout.stages[0].branchRootIds.sort(), ["branch-a", "branch-b"]);
  assert(layout.stages[0].bounds.height > 0);
  assert(layout.stages[0].outcomeBounds);
  assert(layout.stages[0].bounds.y < byId.get("group")!.y);
  assert(layout.stages[0].bounds.y < layout.stages[0].outcomeBounds!.y);
  const stageBottom = layout.stages[0].bounds.y + layout.stages[0].bounds.height;
  const deepestBranchBottom = byId.get("branch-a-audit")!.y + 72;
  assert(stageBottom - deepestBranchBottom >= 28);
  assert(layout.stages[0].outcomeBounds!.y > stageBottom);
  assert(byId.get("success-join")!.y - layout.stages[0].outcomeBounds!.y >= 43.99);
});

test("parallel branches remain inside a stage when no outcome follows the join", () => {
  const root: Node = {
    id: "group",
    functionName: "$tickerq.parallel-group",
    children: [
      leaf("branch-a", "InProgress"),
      leaf("branch-b", "InProgress"),
    ],
  };
  const layout = computeChainFlowLayout(root, {
    nodeWidth: 220,
    horizontalGap: 32,
    verticalGap: 56,
    nodeHeight: node => node.functionName === "$tickerq.parallel-group" ? 48 : 72,
  });

  assert.equal(layout.stages.length, 1);
  assert.equal(layout.merges.length, 1);
  assert.deepEqual(layout.merges[0].sourceIds.sort(), ["branch-a", "branch-b"]);
  assert.deepEqual(layout.merges[0].targetIds, []);
  assert.deepEqual(layout.stages[0].branchRootIds.sort(), ["branch-a", "branch-b"]);
  assert.equal(layout.stages[0].outcomeBounds, undefined);
  assert(layout.stages[0].bounds.y < layout.positions.find(position => position.id === "group")!.y);
  const stageBottom = layout.stages[0].bounds.y + layout.stages[0].bounds.height;
  const branchBottom = Math.max(
    ...layout.positions
      .filter(position => position.id.startsWith("branch-"))
      .map(position => position.y + 72)
  );
  assert(stageBottom - branchBottom >= 28);
  assert(layout.stages[0].bounds.height > 48);
});

test("ordinary chains keep direct parent-child edges", () => {
  const root: Node = {
    id: "root",
    functionName: "Prepare",
    children: [{
      id: "child",
      functionName: "Process",
      runCondition: "OnSuccess",
      children: [leaf("finish")],
    }],
  };
  const layout = computeChainFlowLayout(root, {
    nodeWidth: 220,
    horizontalGap: 32,
    verticalGap: 56,
    nodeHeight: () => 72,
  });
  assert.deepEqual(layout.edges, [
    { fromId: "root", toId: "child" },
    { fromId: "child", toId: "finish" },
  ]);
  assert.deepEqual(layout.merges, []);
  assert.deepEqual(layout.stages, []);
});

test("merge branch inputs receive distinct symmetric ports", () => {
  const ports = Array.from({ length: 4 }, (_, index) =>
    mergePortX(100, index, 4, 60));

  assert.deepEqual(ports, [70, 90, 110, 130]);
  assert.equal(mergePortX(100, 0, 1, 60), 100);
});
