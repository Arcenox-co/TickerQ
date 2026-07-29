import { test } from "node:test";
import assert from "node:assert/strict";

import {
  chainNodeToRequest,
  scheduledLocalToIso,
  tickersToChainNode,
  toDatetimeLocal,
} from "../src/lib/cron/chain-node.ts";
import {
  decodeRequestPayload,
  encodeRequestPayload,
} from "../src/lib/request-payload.ts";
import type { TimeTickerFlatDto } from "../src/services/api-types.ts";

// Full-metadata preservation for the ChainBuilder. Every replaceable field on a
// chain node must survive the flat-DTO -> editor-tree -> request-node round trip:
// retryIntervalsSeconds, onStale, timeoutSeconds, runCondition, description, the
// UTF-8 request payload bytes, and the scheduled root time. These are pure,
// no-op helper tests — no React, no network — so a dropped field fails loudly.

function flat(partial: Partial<TimeTickerFlatDto> & { id: string; functionName: string }): TimeTickerFlatDto {
  return {
    status: "Idle",
    scheduledFor: null,
    elapsedTime: 0,
    retries: 0,
    retryCount: 0,
    priority: "Normal",
    createdAt: "2026-01-01T00:00:00.000Z",
    childCount: 0,
    exceptionMessage: null,
    skippedReason: null,
    parentId: null,
    runCondition: null,
    executedAt: null,
    description: null,
    lockHolder: null,
    lockedAt: null,
    retryIntervalsSeconds: null,
    onStale: "Restart",
    timeoutSeconds: null,
    ...partial,
  };
}

// A multi-node chain: root -> [child-a, child-b]; child-a -> grandchild.
// Distinct values on every node so a field that leaks from another node is caught.
const ROOT_ISO = "2026-08-01T10:30:45.000Z";

const ROOT_JSON = '{"step":"root","msg":"héllo 世界 🚀"}';
const CHILD_A_JSON = '{"step":"child-a","emoji":"✅"}';

const GRANDCHILD_JSON = '{"leaf":1,"note":"ünïcode"}';

function buildFlatChain(): { tickers: TimeTickerFlatDto[]; rootId: string; payloadMap: Map<string, string> } {
  const rootId = "11111111-1111-1111-1111-111111111111";
  const childAId = "22222222-2222-2222-2222-222222222222";
  const childBId = "33333333-3333-3333-3333-333333333333";
  const grandId = "44444444-4444-4444-4444-444444444444";

  const tickers: TimeTickerFlatDto[] = [
    flat({
      id: rootId,
      functionName: "RootJob",
      scheduledFor: ROOT_ISO,
      retries: 3,
      retryIntervalsSeconds: [5, 30, 60],
      onStale: "Cancel",
      timeoutSeconds: 120,
      description: "the root",
    }),
    flat({
      id: childAId,
      functionName: "ChildAJob",
      parentId: rootId,
      runCondition: "OnSuccess",
      retries: 2,
      retryIntervalsSeconds: [10, 20],
      onStale: "Restart",
      timeoutSeconds: 45,
      description: "child a",
    }),
    flat({
      id: childBId,
      functionName: "ChildBJob",
      parentId: rootId,
      runCondition: "OnFailure",
      retries: 0,
      retryIntervalsSeconds: [99],
      onStale: "Cancel",
      timeoutSeconds: null,
      description: "child b",
    }),
    flat({
      id: grandId,
      functionName: "GrandchildJob",
      parentId: childAId,
      runCondition: "OnAnyCompletedStatus",
      retries: 1,
      retryIntervalsSeconds: [7],
      onStale: "Restart",
      timeoutSeconds: 15,
      description: "grandchild",
    }),
  ];

  // The dashboard fetches each node's payload as base64, decodes it, and passes
  // the decoded JSON into tickersToChainNode. Mirror that here so the whole
  // byte round trip (base64 -> text -> editor -> base64) is exercised.
  const payloadMap = new Map<string, string>();
  for (const [id, json] of [
    [rootId, ROOT_JSON],
    [childAId, CHILD_A_JSON],
    [grandId, GRANDCHILD_JSON],
  ] as const) {
    const wire = encodeRequestPayload(json);
    if (wire) payloadMap.set(id, decodeRequestPayload(wire));
  }

  return { tickers, rootId, payloadMap };
}

test("tickersToChainNode carries every replaceable field into the editor tree", () => {
  const { tickers, rootId, payloadMap } = buildFlatChain();
  const tree = tickersToChainNode(tickers, rootId, payloadMap);
  assert.ok(tree, "root node must be built");

  // Root
  assert.equal(tree!.functionName, "RootJob");
  assert.equal(tree!.retries, 3);
  assert.equal(tree!.retryIntervalsSeconds, "5, 30, 60"); // was hardcoded "" (bug)
  assert.equal(tree!.onStale, "Cancel");
  assert.equal(tree!.timeoutSeconds, 120);
  assert.equal(tree!.description, "the root");
  assert.equal(tree!.requestJson, ROOT_JSON);
  assert.equal(tree!.scheduledAt, toDatetimeLocal(ROOT_ISO));

  const [childA, childB] = tree!.children;
  assert.equal(childA.functionName, "ChildAJob");
  assert.equal(childA.runCondition, "OnSuccess");
  assert.equal(childA.retries, 2);
  assert.equal(childA.retryIntervalsSeconds, "10, 20");
  assert.equal(childA.onStale, "Restart");
  assert.equal(childA.timeoutSeconds, 45);
  assert.equal(childA.requestJson, CHILD_A_JSON);

  assert.equal(childB.functionName, "ChildBJob");
  assert.equal(childB.runCondition, "OnFailure");
  assert.equal(childB.retries, 0);
  assert.equal(childB.retryIntervalsSeconds, "99");
  assert.equal(childB.onStale, "Cancel");
  assert.equal(childB.timeoutSeconds, 0); // null timeout -> 0 (no timeout) in the editor
  assert.equal(childB.requestJson, "");

  const grand = childA.children[0];
  assert.equal(grand.functionName, "GrandchildJob");
  assert.equal(grand.runCondition, "OnAnyCompletedStatus");
  assert.equal(grand.retries, 1);
  assert.equal(grand.retryIntervalsSeconds, "7");
  assert.equal(grand.onStale, "Restart");
  assert.equal(grand.timeoutSeconds, 15);
  assert.equal(grand.requestJson, GRANDCHILD_JSON);
});

test("chainNodeToRequest emits every field, base64 request bytes, and skips inactive nodes", () => {
  const { tickers, rootId, payloadMap } = buildFlatChain();
  const tree = tickersToChainNode(tickers, rootId, payloadMap)!;

  const req = chainNodeToRequest(tree);

  assert.equal(req.function, "RootJob");
  assert.equal(req.description, "the root");
  assert.equal(req.retries, 3);
  assert.deepEqual(req.retryIntervalsSeconds, [5, 30, 60]);
  assert.equal(req.onStale, "Cancel");
  assert.equal(req.timeoutSeconds, 120);
  // request is byte[] on the server -> must be base64 of the UTF-8 payload, not raw JSON.
  assert.equal(req.request, encodeRequestPayload(ROOT_JSON));
  assert.equal(decodeRequestPayload(req.request), ROOT_JSON);

  const [ra, rb] = req.children!;
  assert.equal(ra.function, "ChildAJob");
  assert.equal(ra.runCondition, "OnSuccess");
  assert.equal(ra.retries, 2);
  assert.deepEqual(ra.retryIntervalsSeconds, [10, 20]);
  assert.equal(ra.onStale, "Restart");
  assert.equal(ra.timeoutSeconds, 45);
  assert.equal(decodeRequestPayload(ra.request), CHILD_A_JSON);

  assert.equal(rb.function, "ChildBJob");
  assert.equal(rb.runCondition, "OnFailure");
  assert.equal(rb.retries, null);
  assert.deepEqual(rb.retryIntervalsSeconds, [99]); // intervals persist independently of retries
  assert.equal(rb.onStale, "Cancel");
  assert.equal(rb.timeoutSeconds, null); // 0 timeout -> null on the wire
  assert.equal(rb.request, null); // requestless stays null, not empty base64

  const rg = ra.children![0];
  assert.equal(rg.function, "GrandchildJob");
  assert.deepEqual(rg.retryIntervalsSeconds, [7]);
  assert.equal(rg.onStale, "Restart");
  assert.equal(rg.timeoutSeconds, 15);
  assert.equal(decodeRequestPayload(rg.request), GRANDCHILD_JSON);
});

test("inactive child nodes are dropped from the request tree", () => {
  const { tickers, rootId, payloadMap } = buildFlatChain();
  const tree = tickersToChainNode(tickers, rootId, payloadMap)!;
  // Deactivate child-b: it must not appear in the serialized request.
  tree.children[1].active = false;

  const req = chainNodeToRequest(tree);
  assert.equal(req.children!.length, 1);
  assert.equal(req.children![0].function, "ChildAJob");
});

test("scheduled root time survives the datetime-local round trip to the second", () => {
  const local = toDatetimeLocal(ROOT_ISO);
  assert.notEqual(local, "");
  assert.equal(scheduledLocalToIso(local), ROOT_ISO);
});

test("full round trip preserves the exact UTF-8 payload bytes for every node", () => {
  const { tickers, rootId, payloadMap } = buildFlatChain();
  const tree = tickersToChainNode(tickers, rootId, payloadMap)!;
  const req = chainNodeToRequest(tree);

  assert.equal(decodeRequestPayload(req.request), ROOT_JSON);
  assert.equal(decodeRequestPayload(req.children![0].request), CHILD_A_JSON);
  assert.equal(req.children![1].request, null);
  assert.equal(decodeRequestPayload(req.children![0].children![0].request), GRANDCHILD_JSON);
});
