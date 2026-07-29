import test from "node:test";
import assert from "node:assert/strict";
import {
  assertChainLevelFits,
  assertChainTraversalComplete,
  CHAIN_MAX_NODES,
} from "../src/lib/cron/chain-traversal.ts";

test("a 501-node chain fails closed before partial hydration is returned", () => {
  assert.throws(
    () => assertChainLevelFits(CHAIN_MAX_NODES, 1),
    /cannot be edited safely/i,
  );
});

test("an over-depth chain fails closed while descendants remain", () => {
  assert.throws(
    () => assertChainTraversalComplete(1),
    /traversal depth|cannot be edited safely/i,
  );
});

test("a complete bounded chain is accepted", () => {
  assert.doesNotThrow(() => assertChainLevelFits(CHAIN_MAX_NODES - 1, 1));
  assert.doesNotThrow(() => assertChainTraversalComplete(0));
});
