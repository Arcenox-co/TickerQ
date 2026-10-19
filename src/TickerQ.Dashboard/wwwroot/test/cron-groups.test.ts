import assert from "node:assert/strict";
import test from "node:test";
import { CronGroups } from "../src/services/cron-groups.ts";

function connection() {
  const calls: string[][] = [];
  return { calls, async invoke(method: string, id: string) { calls.push([method, id]); } };
}

test("a cron opened before connection startup is subscribed when connected", async () => {
  const groups = new CronGroups();
  const hub = connection();
  await groups.join("cron-a");
  await groups.connect(hub);
  assert.deepEqual(hub.calls, [["JoinGroup", "cron-a"]]);
});

test("reconnect restores mounted crons and excludes pages closed while disconnected", async () => {
  const groups = new CronGroups();
  await groups.connect(connection());
  await groups.join("cron-a");
  await groups.join("cron-b");
  groups.disconnect();
  await groups.leave("cron-a");
  await groups.join("cron-c");
  const next = connection();
  await groups.connect(next);
  assert.deepEqual(next.calls, [["JoinGroup", "cron-b"], ["JoinGroup", "cron-c"]]);
});

test("unmount before startup and StrictMode effect replay leave no ghost subscription", async () => {
  const groups = new CronGroups();
  await groups.join("old-cron");
  await groups.leave("old-cron");
  const hub = connection();
  await groups.connect(hub);
  await Promise.all([groups.join("cron-a"), groups.leave("cron-a"), groups.join("cron-a")]);
  assert.deepEqual(hub.calls, [["JoinGroup", "cron-a"]]);
});

test("one component unmounting does not unsubscribe another subscriber", async () => {
  const groups = new CronGroups();
  const hub = connection();
  await groups.connect(hub);
  await groups.join("cron-a");
  await groups.join("cron-a");
  await groups.leave("cron-a");
  assert.deepEqual(hub.calls, [["JoinGroup", "cron-a"]]);
  await groups.leave("cron-a");
  assert.deepEqual(hub.calls, [["JoinGroup", "cron-a"], ["LeaveGroup", "cron-a"]]);
});

test("leaving during a slow join removes the subscription after the join completes", async () => {
  const groups = new CronGroups();
  const started = Promise.withResolvers<void>();
  const finish = Promise.withResolvers<void>();
  const calls: string[][] = [];
  await groups.connect({ async invoke(method, id) {
    calls.push([method, id]);
    if (method === "JoinGroup") { started.resolve(); await finish.promise; }
  } });
  const join = groups.join("cron-a");
  await started.promise;
  const leave = groups.leave("cron-a");
  finish.resolve();
  await Promise.all([join, leave]);
  assert.deepEqual(calls, [["JoinGroup", "cron-a"], ["LeaveGroup", "cron-a"]]);
});

test("an old in-flight join cannot change a new connection's group membership", async () => {
  const groups = new CronGroups();
  const started = Promise.withResolvers<void>();
  const finish = Promise.withResolvers<void>();
  const oldCalls: string[][] = [];
  await groups.connect({ async invoke(method, id) {
    oldCalls.push([method, id]); started.resolve(); await finish.promise;
  } });
  const oldJoin = groups.join("cron-a");
  await started.promise;
  groups.disconnect();
  await groups.leave("cron-a");
  await groups.join("cron-b");
  const next = connection();
  await groups.connect(next);
  finish.resolve();
  await oldJoin;
  assert.deepEqual(oldCalls, [["JoinGroup", "cron-a"]]);
  assert.deepEqual(next.calls, [["JoinGroup", "cron-b"]]);
});

test("a failed join is retried when the connection is restored", async () => {
  const groups = new CronGroups();
  await groups.join("cron-a");
  await assert.rejects(groups.connect({ async invoke() { throw new Error("connection lost"); } }));
  const next = connection();
  await groups.connect(next);
  assert.deepEqual(next.calls, [["JoinGroup", "cron-a"]]);
});
