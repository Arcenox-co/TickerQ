import test from "node:test";
import assert from "node:assert/strict";
import {
  editableDestination,
  parseHeaders,
  removeDestination,
} from "../src/components/integrations/configuration.ts";
import type { IntegrationConfiguration } from "../src/services/integration-types.ts";

test("redacted headers are preserved unless explicitly replaced or cleared", () => {
  assert.equal(parseHeaders(""), null);
  assert.equal(parseHeaders(" \n "), null);
  assert.deepEqual(parseHeaders("{}"), {});
  assert.deepEqual(parseHeaders('{"Authorization":"Bearer value"}'), {
    Authorization: "Bearer value",
  });
});

test("invalid header shapes cannot be submitted as configuration", () => {
  for (const text of [
    "{",
    "null",
    "[]",
    '"token"',
    '{"X-Test":123}',
    '{"X-Test":null}',
  ])
    assert.throws(() => parseHeaders(text));
});

test("removing an integration preserves shared rules and removes only orphaned rules without changing the snapshot", () => {
  const config: IntegrationConfiguration = {
    version: "v1",
    destinations: ["slack", "teams"].map((id) => ({
      id,
      name: id,
      provider: id,
      enabled: true,
      url: null,
      headers: null,
      payloadTemplate: null,
    })),
    monitors: [
      { id: "sole-monitor", destinationIds: ["slack"] },
      { id: "shared-monitor", destinationIds: ["slack", "teams"] },
    ].map((m) => ({
      ...m,
      name: m.id,
      function: "Invoices",
      kind: "deadline" as const,
      enabled: true,
      notifyRecovery: true,
      timeZoneId: "UTC",
      deadlineLocalTime: "08:00",
      graceMinutes: 10,
      durationSeconds: 300,
      failureThreshold: 3,
      lookbackHours: 24,
    })),
    rules: [
      { id: "sole", destinationIds: ["slack"] },
      { id: "shared", destinationIds: ["slack", "teams"] },
      { id: "other", destinationIds: ["teams"] },
    ].map((r) => ({
      ...r,
      name: r.id,
      enabled: true,
      events: ["failed"],
      includeFunctions: [],
      excludeFunctions: [],
      cooldownSeconds: 60,
    })),
  };
  const original = structuredClone(config);
  const next = removeDestination(config, "slack");
  assert.equal(next.version, "v1");
  assert.deepEqual(
    next.destinations.map((d) => d.id),
    ["teams"],
  );
  assert.deepEqual(
    next.rules.map((r) => [r.id, r.destinationIds]),
    [
      ["shared", ["teams"]],
      ["other", ["teams"]],
    ],
  );
  assert.deepEqual(
    next.monitors?.map((m) => [m.id, m.destinationIds]),
    [["shared-monitor", ["teams"]]],
  );
  assert.deepEqual(config, original);
});


test("editing legacy payloads preserves all three message categories without mutating the snapshot", () => {
  const original = { id: "slack", name: "Slack", provider: "slack", enabled: true, url: null, headers: null,
    payloadTemplate: '{"text":"existing message"}' };
  const editable = editableDestination(original);
  assert.equal(editable.payloadTemplate, null);
  assert.deepEqual(editable.messageTemplates, { jobAlert: original.payloadTemplate, monitoringAlert: original.payloadTemplate, recovery: original.payloadTemplate });
  editable.messageTemplates!.recovery = null;
  assert.equal(editable.messageTemplates!.jobAlert, original.payloadTemplate);
  assert.equal(original.payloadTemplate, '{"text":"existing message"}');
});

test("explicit default categories never fall back to a stale shared template", () => {
  const editable = editableDestination({ id: "slack", name: "Slack", provider: "slack", enabled: true, url: null, headers: null,
    payloadTemplate: '{"text":"old"}', messageTemplates: { jobAlert: null, monitoringAlert: '{"text":"monitor"}', recovery: null } });
  assert.equal(editable.messageTemplates!.jobAlert, null);
  assert.equal(editable.messageTemplates!.recovery, null);
  assert.equal(editable.messageTemplates!.monitoringAlert, '{"text":"monitor"}');
});
