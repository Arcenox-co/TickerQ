import test from "node:test";
import assert from "node:assert/strict";
import { isChainDeletionRestricted } from "../src/lib/cron/chain-deletion.ts";
import type { TimeTickerFlatDto, TickerStatus } from "../src/services/api-types.ts";

const root = { id: "root", deleteBehavior: "Restrict", status: "Done", parentId: null } as TimeTickerFlatDto;
const child = { id: "child", parentId: "root", status: "Done" } as TimeTickerFlatDto;
const grandchild = (status: TickerStatus) => ({ id: "grandchild", parentId: "child", status }) as TimeTickerFlatDto;

test("Restrict waits for unfinished descendants at any depth", () => {
  for (const status of ["Idle", "Queued", "InProgress"] as const) {
    assert.equal(isChainDeletionRestricted(root, [root, child, grandchild(status)]), true);
  }
});
test("Restrict automatically releases for all final statuses", () => {
  for (const status of ["Done", "DueDone", "Failed", "Cancelled", "Skipped"] as const) {
    assert.equal(isChainDeletionRestricted(root, [root, child, grandchild(status)]), false);
  }
});
test("standalone roots and Cascade do not impose a descendant restriction", () => {
  assert.equal(isChainDeletionRestricted(root, [root]), false);
  assert.equal(isChainDeletionRestricted({ ...root, deleteBehavior: "Cascade" }, [root, child, grandchild("Idle")]), false);
});
test("a retry returning a descendant to Queued reapplies Restrict", () => {
  const chain = [root, child, grandchild("Failed")];
  assert.equal(isChainDeletionRestricted(root, chain), false);
  chain[2] = grandchild("Queued");
  assert.equal(isChainDeletionRestricted(root, chain), true);
});
