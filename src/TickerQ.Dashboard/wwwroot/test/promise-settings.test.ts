import test from "node:test";
import assert from "node:assert/strict";

import { autoFixFollowsCode, durationLabel, effectiveWithin, withinChange, withinChoices } from "../src/lib/promise-settings.ts";
import type { PromiseSettings } from "../src/services/promise-types.ts";

const settings = (patch: Partial<PromiseSettings> = {}): PromiseSettings =>
  ({ paused: false, withinSeconds: null, withinSecondsFromCode: 300, ...patch });

test("durations read as people say them", () => {
  assert.equal(durationLabel(60), "1 minute");
  assert.equal(durationLabel(300), "5 minutes");
  assert.equal(durationLabel(3600), "1 hour");
  assert.equal(durationLabel(7200), "2 hours");
  assert.equal(durationLabel(86400), "1 day");
  assert.equal(durationLabel(172800), "2 days");
  assert.equal(durationLabel(90), "90 seconds");
  assert.equal(durationLabel(20), "20 seconds");
  assert.equal(durationLabel(1), "1 second");
});

test("the code's time limit applies until the dashboard sets one", () => {
  assert.equal(effectiveWithin(settings()), 300);
  assert.equal(effectiveWithin(settings({ withinSeconds: 60 })), 60);
});

test("choices include the presets and the code's value, marked, with the current one selected", () => {
  const choices = withinChoices(settings());
  assert.deepEqual(choices.map(c => c.label), ["1 minute", "5 minutes", "15 minutes", "1 hour", "1 day"]);
  assert.deepEqual(choices.filter(c => c.fromCode).map(c => c.seconds), [300]);
  assert.deepEqual(choices.filter(c => c.selected).map(c => c.seconds), [300]);

  const custom = withinChoices(settings({ withinSecondsFromCode: 20, withinSeconds: 7200 }));
  assert.deepEqual(custom.map(c => c.seconds), [20, 60, 300, 900, 3600, 7200, 86400]);
  assert.equal(custom.find(c => c.fromCode)?.label, "20 seconds");
  assert.equal(custom.find(c => c.selected)?.label, "2 hours");
});

test("picking the code's value goes back to it instead of copying it", () => {
  assert.deepEqual(withinChange(settings({ withinSeconds: 60 }), 300), { withinSeconds: null });
  assert.deepEqual(withinChange(settings(), 900), { withinSeconds: 900 });
});

test("automatic fixing follows the code when it matches", () => {
  const autoFix = { maxPerCheck: 50, paused: false, pausedReason: null };
  assert.equal(autoFixFollowsCode({ ...autoFix, enabled: true, fromCode: true }), true);
  assert.equal(autoFixFollowsCode({ ...autoFix, enabled: false, fromCode: false }), true);
  assert.equal(autoFixFollowsCode({ ...autoFix, enabled: true, fromCode: false }), false);
});
