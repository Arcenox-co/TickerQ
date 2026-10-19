import assert from "node:assert/strict";
import test from "node:test";
import { resolveChainLockHolder } from "../src/lib/cron/lock-holder.ts";

const root = { id: "root", parentId: null, lockHolder: "worker-root" };
const child = { id: "child", parentId: "root", lockHolder: null };
const grandchild = { id: "grandchild", parentId: "child", lockHolder: null };

test("a job's own holder takes precedence over its ancestors", () => {
  assert.deepEqual(resolveChainLockHolder("child", [root, { ...child, lockHolder: "worker-child" }]),
    { value: "worker-child", inherited: false });
  assert.deepEqual(resolveChainLockHolder("root", [root, child]), { value: "worker-root", inherited: false });
});

test("children inherit the nearest populated ancestor including the chain root", () => {
  assert.deepEqual(resolveChainLockHolder("grandchild", [root, child, grandchild]),
    { value: "worker-root", inherited: true });
  assert.deepEqual(resolveChainLockHolder("grandchild", [root, { ...child, lockHolder: "worker-parent" }, grandchild]),
    { value: "worker-parent", inherited: true });
});

test("ownership changes and releases are reflected without keeping an old holder", () => {
  assert.equal(resolveChainLockHolder("child", [{ ...root, lockHolder: "replacement" }, child]).value, "replacement");
  assert.deepEqual(resolveChainLockHolder("child", [{ ...root, lockHolder: null }, child]),
    { value: null, inherited: false });
});

test("missing parents, unknown jobs and cycles never borrow an unrelated holder", () => {
  assert.equal(resolveChainLockHolder("grandchild", [root, grandchild]).value, null);
  assert.equal(resolveChainLockHolder("missing", [root, child]).value, null);
  assert.equal(resolveChainLockHolder("child", [root, { ...child, parentId: "grandchild" }, grandchild]).value, null);
});
