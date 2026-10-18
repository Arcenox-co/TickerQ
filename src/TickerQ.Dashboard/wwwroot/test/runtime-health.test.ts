import assert from "node:assert/strict";
import test from "node:test";
import { getRuntimeHealth } from "../src/lib/runtime-health.ts";
import type { RuntimeDiagnostics } from "../src/services/api-types";

const host = { isRunning: true, activeThreads: 1, maxConcurrency: 4 };
const before = "2026-10-17T09:00:00Z";
const after = "2026-10-17T09:30:00Z";
const diagnostics: RuntimeDiagnostics = {
  node: "test", fallbackScanFailures: 2,
  lastSuccessfulFallbackScanUtc: after, lastFallbackFailureUtc: before,
  executionLogs: { writesAvailable: true, pendingWrites: 2, droppedUnflushedExecutions: 0,
    writeFailures: 3, lastSuccessfulFlushUtc: after, lastFailureUtc: before },
};

test("recovered scan/write failures and a pending flush do not imply a current outage", () => {
  const health = getRuntimeHealth({ host, diagnostics, persistLogs: true });
  assert.equal(health.status, "healthy");
  assert.deepEqual(health.issues, []);
  assert.equal(getRuntimeHealth({ host, diagnostics: { ...diagnostics,
    lastFallbackFailureUtc: after, lastSuccessfulFallbackScanUtc: before }, persistLogs: true }).scanFailing, true);
});

test("scan timestamps compare instants, including different UTC offsets", () => {
  const health = getRuntimeHealth({ host, diagnostics: { ...diagnostics,
    lastSuccessfulFallbackScanUtc: "2026-10-17T10:00:00+02:00", lastFallbackFailureUtc: before }, persistLogs: false });
  assert.equal(health.status, "warning");
  assert.equal(health.scanFailing, true);
});

test("only configured database persistence contributes write problems", () => {
  const failed = { ...diagnostics, executionLogs: { ...diagnostics.executionLogs,
    writesAvailable: false, droppedUnflushedExecutions: 2 } };
  const health = getRuntimeHealth({ host, diagnostics: failed, persistLogs: true });
  assert.equal(health.status, "warning");
  assert.equal(health.issues.length, 2);
  assert.equal(getRuntimeHealth({ host, diagnostics: failed, persistLogs: false }).status, "healthy");
  assert.equal(getRuntimeHealth({ host: { ...host, isRunning: false }, diagnostics, persistLogs: false }).status, "warning");
});

test("missing data, an incomplete first scan, or a failed refresh cannot report healthy", () => {
  assert.equal(getRuntimeHealth({ host, diagnostics, persistLogs: true, unavailable: true }).label, "Status unavailable");
  assert.equal(getRuntimeHealth({ host, persistLogs: true }).status, "unknown");
  assert.equal(getRuntimeHealth({ host, diagnostics }).status, "unknown");
  assert.equal(getRuntimeHealth({ host, diagnostics: { ...diagnostics,
    lastSuccessfulFallbackScanUtc: null, lastFallbackFailureUtc: null }, persistLogs: false }).status, "unknown");
});
