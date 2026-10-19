import assert from "node:assert/strict";
import test from "node:test";
import { hasCompletion, logColor, mapLogLine } from "../src/lib/cron/log-format.ts";
import { STATUS_CONFIG } from "../src/lib/cron/status-config.ts";
import type { TickerLogLine } from "../src/services/api-types";

const line = (patch: Partial<TickerLogLine> = {}): TickerLogLine => ({
  unixMs: 1, level: 2, source: "", message: "message", category: "test", functionName: "Example", ...patch,
});

test("only explicit lifecycle metadata uses status colors; application info remains neutral", () => {
  assert.equal(logColor(mapLogLine(line({ status: "Done" }))), "text-foreground");
  assert.equal(logColor(mapLogLine(line({ level: 4 }))), "text-status-error");
  assert.equal(logColor(mapLogLine(line({ level: 3 }))), "text-status-warning");
  assert.equal(logColor(mapLogLine(line({ level: 1 }))), "text-muted-foreground");
  for (const status of Object.keys(STATUS_CONFIG) as (keyof typeof STATUS_CONFIG)[]) {
    assert.equal(logColor(mapLogLine(line({ lifecycleEvent: "Completed", status }))), STATUS_CONFIG[status].text);
  }
});

test("chain entries preserve both function and scheduler origin", () => {
  assert.equal(mapLogLine(line({ source: "scheduler" }), "Child").message, "[Child · scheduler] message");
  assert.equal(mapLogLine(line(), "Child").message, "[Child] message");
});

test("saved failure fallback is suppressed only after the latest lifecycle outcome", () => {
  const completed = line({ lifecycleEvent: "Completed", status: "Failed" });
  assert.equal(hasCompletion([completed, line()], "Failed"), true);
  assert.equal(hasCompletion([completed], "Cancelled"), false);
  assert.equal(hasCompletion([completed, line({ lifecycleEvent: "Started", status: "InProgress" })], "Failed"), false);
  assert.equal(hasCompletion([], "Failed"), false);
});
