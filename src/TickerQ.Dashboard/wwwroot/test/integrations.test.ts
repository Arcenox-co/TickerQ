import test from "node:test";
import assert from "node:assert/strict";
import {
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
  assert.deepEqual(config, original);
});
