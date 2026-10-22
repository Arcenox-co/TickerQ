import { test } from "node:test";
import assert from "node:assert/strict";
import { applyAssistantProposal } from "../src/lib/apply-assistant-proposal.ts";
import { parseAssistantEvent } from "../src/lib/assistant-stream.ts";
import type { AssistantProposal } from "../src/services/assistant-api.ts";
import type { AssistantActionApi } from "../src/lib/apply-assistant-proposal.ts";

const proposal = (overrides: Partial<AssistantProposal> = {}): AssistantProposal => ({
  kind: "retryExecution", function: "Root", expression: "", currentExpression: "", executionTime: "",
  retries: null, description: "", requestJson: "", targetId: "execution-1", isEnabled: null, chainJson: "",
  targetType: "TimeTicker", expectedStatus: "Failed", expectedExecutedAt: null, expectedLockedAt: null, ...overrides,
});
const current = { id: "execution-1", functionName: "Root", status: "Failed", executedAt: null, lockedAt: null };
function fakeApi(values: Record<string, unknown> = {}) {
  const calls: { method: string; args: unknown[] }[] = [];
  const api = new Proxy({}, { get: (_, method: string) => async (...args: unknown[]) => {
    calls.push({ method, args });
    const result = values[method];
    if (result instanceof Error) throw result;
    return result;
  } }) as AssistantActionApi;
  return { api, calls };
}

test("retry rechecks the exact time ticker before requesting a run", async () => {
  const { api, calls } = fakeApi({ getTimeTicker: current });
  assert.equal((await applyAssistantProposal(proposal(), api)).message, "Run requested");
  assert.deepEqual(calls.map(x => x.method), ["getTimeTicker", "runTimeTicker"]);
  assert.deepEqual(calls[1].args, ["execution-1"]);
});

for (const change of [{ status: "InProgress" }, { functionName: "Other" }, { executedAt: "2026-10-19T08:00:00Z" }, { lockedAt: "2026-10-19T08:00:00Z" }]) {
  test(`stale execution proposal rejects ${Object.keys(change)[0]} change without writing`, async () => {
    const { api, calls } = fakeApi({ getTimeTicker: { ...current, ...change } });
    await assert.rejects(applyAssistantProposal(proposal(), api), /execution changed/);
    assert.deepEqual(calls.map(x => x.method), ["getTimeTicker"]);
  });
}

test("cron retry revives the exact occurrence without creating another occurrence", async () => {
  const { api, calls } = fakeApi({ getCronOccurrence: { ...current, cronTickerId: "cron-1" } });
  await applyAssistantProposal(proposal({ targetType: "CronOccurrence", cronTickerId: "cron-1" }), api);
  assert.deepEqual(calls.map(x => x.method), ["getCronOccurrence", "rerunCronOccurrence"]);
  assert.deepEqual(calls[1].args, ["execution-1"]);
});

for (const state of ["Idle", "Queued", "InProgress"]) {
  test(`cancellation selects correct endpoint semantics for ${state}`, async () => {
    const { api, calls } = fakeApi({ getTimeTicker: { ...current, status: state },
      cancelTicker: { outcome: state === "InProgress" ? "Requested" : "Cancelled", affected: 1 } });
    const message = (await applyAssistantProposal(proposal({ kind: "cancelExecution", expectedStatus: state }), api)).message;
    assert.deepEqual(calls[1].args, [{ id: "execution-1", type: "TimeTicker", running: state === "InProgress" }]);
    assert.match(message, state === "InProgress" ? /requested.*handler/ : /cancelled/);
  });
}

test("zero affected cancellations are not reported as applied", async () => {
  const { api } = fakeApi({ getTimeTicker: { ...current, status: "Idle" }, cancelTicker: { outcome: "Cancelled", affected: 0 } });
  await assert.rejects(applyAssistantProposal(proposal({ kind: "cancelExecution", expectedStatus: "Idle" }), api), /No execution was cancelled/);
});

for (const targetType of ["TimeTicker", "CronOccurrence", "CronTicker"] as const) {
  test(`delete uses only the ${targetType} endpoint`, async () => {
    const { api, calls } = fakeApi({ getTimeTicker: current, getCronOccurrence: current, getCronTicker: { functionName: "Root", expression: "" } });
    await applyAssistantProposal(proposal({ kind: "deleteExecution", targetType }), api);
    assert.equal(calls.at(-1)?.method, `delete${targetType}`);
    assert.deepEqual(calls.at(-1)?.args, ["execution-1"]);
  });
}

test("API failures remain failures and unknown proposals never silently succeed", async () => {
  const { api, calls } = fakeApi({ getTimeTicker: current, runTimeTicker: new Error("conflict") });
  await assert.rejects(applyAssistantProposal(proposal(), api), /conflict/);
  const count = calls.length;
  await assert.rejects(applyAssistantProposal(proposal({ kind: "unknown" as never }), api), /Unsupported proposal/);
  assert.equal(calls.length, count);
});

test("cron updates patch requested fields without resetting retry intervals or chain options", async () => {
  const { api, calls } = fakeApi({ getCronTicker: { functionName: "Root", expression: "0 8 * * *" } });
  await applyAssistantProposal(proposal({ kind: "updateCronTicker", expression: "0 9 * * *", currentExpression: "0 8 * * *" }), api);
  const body = calls[1].args[1];
  assert.deepEqual(JSON.parse(JSON.stringify(body)), { expression: "0 9 * * *" });
});

test("one-time job proposals save a non-executable draft and return its builder URL", async () => {
  const { api, calls } = fakeApi({ createDefinition: { id: "draft-job-1" } });
  const result = await applyAssistantProposal(proposal({
    kind: "createTimeTickerDraft",
    draftName: "Invoice reminder",
    function: "Root",
    executionTime: "2026-12-01T09:30:00Z",
    retries: 2,
    description: "Send the reminder",
    requestJson: '{"invoiceId":42}',
  }), api);

  assert.deepEqual(calls.map(x => x.method), ["createDefinition"]);
  const body = calls[0].args[0] as any;
  assert.equal(body.kind, "Draft");
  assert.equal(body.target, "TimeTicker");
  assert.equal(body.name, "Invoice reminder");
  assert.equal(body.payload.executionTime, "2026-12-01T09:30:00Z");
  assert.equal(body.payload.root.function, "Root");
  assert.equal(body.payload.root.retries, 2);
  assert.equal(Buffer.from(body.payload.root.request, "base64").toString(), '{"invoiceId":42}');
  assert.deepEqual(body.payload.root.children, []);
  assert.deepEqual(result, {
    message: "Job draft saved",
    href: "/time-tickers/new-chain?draft=draft-job-1",
    linkLabel: "Open draft",
  });
});

test("one-time chain proposals preserve the tree in a saved draft without creating executions", async () => {
  const { api, calls } = fakeApi({ createDefinition: { id: "draft-chain-1" } });
  const result = await applyAssistantProposal(proposal({
    kind: "createChainDraft",
    draftName: "Invoice workflow",
    executionTime: "2026-12-01T09:30:00Z",
    chainJson: JSON.stringify({ function: "Root", retries: 0, requestJson: '{"invoiceId":42}', children: [
      { function: "Child", retries: 1, runCondition: "OnSuccess", requestJson: "", children: [] },
    ] }),
  }), api);

  assert.deepEqual(calls.map(x => x.method), ["createDefinition"]);
  const body = calls[0].args[0] as any;
  assert.equal(body.name, "Invoice workflow");
  assert.equal(body.payload.root.function, "Root");
  assert.equal(body.payload.root.children[0].function, "Child");
  assert.equal(body.payload.root.children[0].runCondition, "OnSuccess");
  assert.equal(result.href, "/time-tickers/new-chain?draft=draft-chain-1");
});

test("recurring chain separates root payload from children and preserves run conditions", async () => {
  const { api, calls } = fakeApi();
  await applyAssistantProposal(proposal({ kind: "createCronChain", expression: "0 8 * * *", skipIfAlreadyRunning: true,
    chainJson: JSON.stringify({ function: "Root", retries: 2, requestJson: '{"hello":"世界"}', children: [
      { function: "Child", retries: 1, runCondition: "OnFailure", requestJson: '{"emoji":"✅"}', children: [] }] }) }), api);
  const body = calls[0].args[0] as any;
  assert.equal(calls[0].method, "addCronTicker");
  assert.equal(body.function, "Root");
  assert.equal(body.skipIfAlreadyRunning, true);
  assert.equal(body.chain.deleteBehavior, "Restrict");
  assert.equal(body.chain.children[0].function, "Child");
  assert.equal(body.chain.children[0].runCondition, "OnFailure");
  assert.equal(Buffer.from(body.request, "base64").toString(), '{"hello":"世界"}');
  assert.equal(Buffer.from(body.chain.children[0].request, "base64").toString(), '{"emoji":"✅"}');
});

const settings = { version: "version-1", destinations: [{ id: "slack", url: null, headers: null, payloadTemplate: "custom-template" }],
  rules: [{ id: "existing-rule" }], monitors: [{ id: "existing-monitor" }] };
const rule = { id: "new-rule", name: "Failures", enabled: true, destinationIds: ["slack"], events: ["failed"], includeFunctions: ["Root"], excludeFunctions: [], cooldownSeconds: 60 };

test("integration confirm preserves other settings and includes version for server compare-and-swap", async () => {
  const { api, calls } = fakeApi({ getIntegrations: { canManage: true, configuration: settings } });
  await applyAssistantProposal(proposal({ kind: "createAlertRule", configurationVersion: "version-1", rule }), api);
  assert.deepEqual(calls.map(x => x.method), ["getIntegrations", "saveIntegrations"]);
  assert.deepEqual(calls[1].args, [{ ...settings, rules: [...settings.rules, rule] }]);
  assert.equal(settings.rules.length, 1);
});

for (const snapshot of [{ canManage: false, configuration: settings }, { canManage: true, configuration: { ...settings, version: "version-2" } }]) {
  test(`integration settings reject stale version or missing permission: ${snapshot.canManage}`, async () => {
    const { api, calls } = fakeApi({ getIntegrations: snapshot });
    await assert.rejects(applyAssistantProposal(proposal({ kind: "createAlertRule", configurationVersion: "version-1", rule }), api));
    assert.deepEqual(calls.map(x => x.method), ["getIntegrations"]);
  });
}

test("server stream errors are propagated instead of silently swallowed", () => {
  assert.throws(() => parseAssistantEvent('{"type":"error","data":"Provider unavailable"}'), /Provider unavailable/);
  assert.deepEqual(parseAssistantEvent('{"type":"done","data":""}'), { type: "done", data: "" });
  assert.throws(() => parseAssistantEvent('{"type":"proposal"}'), /Invalid assistant response/);
});
