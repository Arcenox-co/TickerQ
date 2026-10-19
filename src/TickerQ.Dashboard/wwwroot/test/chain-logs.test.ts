import assert from "node:assert/strict";
import test from "node:test";
import { fetchChainLogs, needsChainLogRefresh, CHAIN_LOG_LINE_LIMIT } from "../src/lib/cron/chain-logs.ts";
import type { TimeTickerFlatDto, TickerLogBatchItem, TickerLogLine } from "../src/services/api-types";

const job = (id: string, status = "Done") => ({ id, status }) as TimeTickerFlatDto;
const line = (id: string, time = 1): TickerLogLine => ({ id, unixMs: time, message: "log", level: 2, source: "", category: "test", functionName: "test" });
const reply = (id: string, lines: TickerLogLine[] = []): TickerLogBatchItem => ({ tickerId: id, cursor: id, reset: true, tail: { lines } });

test("100 jobs use two bounded requests; completed jobs stop polling independently", async () => {
  const list = Array.from({ length: 100 }, (_, i) => job(String(i)));
  const calls: string[][] = [];
  const fetch = async (items: { tickerId: string }[]) => { calls.push(items.map(x => x.tickerId)); return { items: items.map(x => reply(x.tickerId)) }; };
  let state = await fetchChainLogs(list, undefined, fetch);
  assert.deepEqual(calls.map(c => c.length), [50, 50]);
  for (let i = 0; i < 3; i++) state = await fetchChainLogs(list, state, fetch);
  assert.equal(needsChainLogRefresh(list, state), false);
  calls.length = 0;
  const changed = [...list, job("new", "InProgress")];
  state = await fetchChainLogs(changed, state, fetch);
  assert.deepEqual(calls, [["new"]]);
  calls.length = 0;
  await fetchChainLogs([...list, job("new")], state, fetch);
  assert.deepEqual(calls, [["new"]]);
});

test("append, reset and late arrivals keep the correct log tail", async () => {
  const list = [job("a", "InProgress")];
  let state = await fetchChainLogs(list, undefined, async () => ({ items: [reply("a", [line("1")])] }));
  state = await fetchChainLogs(list, state, async items => {
    assert.equal(items[0].cursor, "a");
    return { items: [{ ...reply("a", [line("2", 2)]), reset: false }] };
  });
  assert.deepEqual(state.entries.a.tail.lines.map(l => l.id), ["1", "2"]);
  state = await fetchChainLogs(list, state, async () => ({ items: [reply("a", [line("late", 0), line("2", 2)])] }));
  assert.deepEqual(state.entries.a.tail.lines.map(l => l.id), ["late", "2"]);
});

test("aggregate chain display is bounded and manual refresh includes settled jobs", async () => {
  const list = Array.from({ length: 10 }, (_, i) => job(String(i)));
  const fetch = async (items: { tickerId: string }[]) => ({ items: items.map(x => reply(x.tickerId,
    Array.from({ length: 200 }, (_, i) => line(`${x.tickerId}-${i}`, i)))) });
  const state = await fetchChainLogs(list, undefined, fetch);
  assert.equal(Object.values(state.entries).reduce((sum, e) => sum + e.tail.lines.length, 0), CHAIN_LOG_LINE_LIMIT);
  assert.equal(state.displayTruncated, true);
  for (const entry of Object.values(state.entries)) entry.remaining = 0;
  let requested = 0;
  await fetchChainLogs(list, state, async items => { requested += items.length; return fetch(items); }, true);
  assert.equal(requested, 10);
});

test("write health reflects the latest batch rather than an old settled job", async () => {
  const health = (writesAvailable: boolean) => ({ writesAvailable, pendingWrites: 0, droppedUnflushedExecutions: 0,
    writeFailures: 1, lastSuccessfulFlushUtc: null, lastFailureUtc: null });
  const initial = reply("old"); initial.tail.writeHealth = health(false);
  let state = await fetchChainLogs([job("old")], undefined, async () => ({items:[initial]}));
  state.entries.old.remaining = 0;
  const recovered = reply("active"); recovered.tail.writeHealth = health(true);
  state = await fetchChainLogs([job("old"), job("active", "InProgress")], state, async () => ({items:[recovered]}));
  assert.equal(state.entries.old.tail.writeHealth?.writesAvailable, false);
  assert.equal(state.writeHealth?.writesAvailable, true);
});

test("large messages also obey the aggregate text-byte limit", async () => {
  const lines = Array.from({ length: 200 }, (_, i) => ({ ...line(String(i), i), message: "x".repeat(16 * 1024) }));
  const state = await fetchChainLogs([job("a")], undefined, async () => ({ items: [reply("a", lines)] }));
  assert.equal(state.displayTruncated,true);
  assert(state.entries.a.tail.lines.reduce((bytes,line)=>bytes+new TextEncoder().encode(line.message).length,0)<=1024*1024);
});
