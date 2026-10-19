import assert from "node:assert/strict";
import test from "node:test";
import { cronToChainNode, chainNodeToCronTemplate, canViewOccurrenceChain, cronOccurrenceChainPreview } from "../src/lib/cron/cron-chain.ts";
import type { CronOccurrenceFlatDto, CronTickerFlatDto } from "../src/services/api-types.ts";

test("recurring chain round-trip preserves nested payloads and execution policies", () => {
  const cron = { functionName: "Prepare", description: "Daily reports", retries: 2,
    retryIntervalsSeconds: [1, 5], onStale: "Restart", timeoutSeconds: 40,
    skipIfAlreadyRunning: true, chain: { deleteBehavior: "Restrict", children: [{
      function: "Send", request: "eyJvayI6dHJ1ZX0=", retries: 3, retryIntervals: [2, 7],
      runCondition: "OnSuccess", onStale: "Cancel", timeoutSeconds: 9,
      children: [{ function: "Audit", retries: 0, runCondition: "OnAnyCompletedStatus", onStale: "Restart", children: [] }],
    }] } } as CronTickerFlatDto;
  const root = cronToChainNode(cron, "eyJpZCI6MX0=");
  assert.equal(root.requestJson, '{"id":1}');
  assert.equal(root.children[0].requestJson, '{"ok":true}');
  const saved = chainNodeToCronTemplate(root);
  assert.equal(saved.deleteBehavior, "Restrict");
  assert.equal(saved.children[0].request, cron.chain!.children[0].request);
  assert.deepEqual(saved.children[0].retryIntervals, [2, 7]);
  assert.equal(saved.children[0].timeoutSeconds, 9);
  assert.equal(saved.children[0].onStale, "Cancel");
  assert.equal(saved.children[0].children[0].runCondition, "OnAnyCompletedStatus");
  root.children[0].active = false;
  assert.deepEqual(chainNodeToCronTemplate(root).children, []);
});

const previewCron = { id: "cron", functionName: "Prepare", description: "Monthly report", priority: "Normal",
  retries: 2, retryIntervalsSeconds: [1, 5], onStale: "Restart", timeoutSeconds: 40,
  chain: { deleteBehavior: "Restrict", children: [{
    function: "Send", retries: 3, retryIntervals: [2, 7], runCondition: "OnSuccess", onStale: "Cancel", timeoutSeconds: 9,
    children: [{ function: "Audit", retries: 0, runCondition: "OnAnyCompletedStatus", onStale: "Restart", children: [] }],
  }, { function: "Recover", retries: 0, runCondition: "OnFailure", onStale: "Restart", children: [] }] },
} as CronTickerFlatDto;
const previewOccurrence = { id: "occurrence", cronTickerId: "cron", status: "Queued",
  scheduledFor: "2026-10-18T18:00:00Z", createdAt: "2026-10-18T17:59:00Z",
} as CronOccurrenceFlatDto;

test("pending cron chain shows the entire idle tree with conditions and policies without execution data", () => {
  const nodes = cronOccurrenceChainPreview(previewCron, previewOccurrence);
  assert.equal(nodes.length, 4);
  assert.equal(nodes[0].id, previewOccurrence.id);
  assert.equal(nodes[0].scheduledFor, previewOccurrence.scheduledFor);
  assert.equal(nodes[0].deleteBehavior, "Restrict");
  assert.equal(nodes[0].childCount, 2);
  assert.equal(nodes[1].parentId, nodes[0].id);
  assert.equal(nodes[2].parentId, nodes[1].id);
  assert.equal(nodes[3].parentId, nodes[0].id);
  assert.equal(nodes[1].runCondition, "OnSuccess");
  assert.equal(nodes[2].runCondition, "OnAnyCompletedStatus");
  assert.equal(nodes[3].runCondition, "OnFailure");
  assert.deepEqual(nodes[1].retryIntervalsSeconds, [2, 7]);
  assert.equal(nodes[1].timeoutSeconds, 9);
  assert.equal(nodes[1].onStale, "Cancel");
  assert.equal(new Set(nodes.map(n => n.id)).size, nodes.length);
  assert(nodes.every(n => n.status === "Idle" && n.executedAt === null && n.elapsedTime === 0 && n.lockHolder === null));
  assert(nodes.slice(1).every(n => n.scheduledFor === null));
});

test("only active matching occurrences use a template; issued chains keep their persisted history", () => {
  for (const status of ["Idle", "Queued", "InProgress"] as const) {
    assert(canViewOccurrenceChain(previewCron, { ...previewOccurrence, status }));
  }
  for (const status of ["Done", "DueDone", "Failed", "Cancelled", "Skipped"] as const) {
    const occurrence = { ...previewOccurrence, status };
    assert.equal(canViewOccurrenceChain(previewCron, occurrence), false);
    assert.deepEqual(cronOccurrenceChainPreview(previewCron, occurrence), []);
  }
  assert.equal(canViewOccurrenceChain({ ...previewCron, chain: null }, previewOccurrence), false);
  assert.deepEqual(cronOccurrenceChainPreview(previewCron, { ...previewOccurrence, cronTickerId: "another-cron" }), []);
  const issued = { ...previewOccurrence, status: "Done" as const, chainRootId: "actual-root" };
  assert(canViewOccurrenceChain(undefined, issued));
  assert.deepEqual(cronOccurrenceChainPreview(previewCron, issued), []);
});
