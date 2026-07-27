import { test } from "node:test";
import assert from "node:assert/strict";

import { classifyExampleAction } from "../src/lib/request-schema.ts";

// The single-example button changes meaning with the current payload:
//   fill     — blank payload, nothing to lose
//   matching — payload already JSON-equivalent to the example (no-op)
//   reset    — different, or non-blank-but-unparseable, data an overwrite clobbers
const example = '{"name":"Ada","count":3}';

test("blank payload classifies as fill", () => {
  assert.equal(classifyExampleAction("", example), "fill");
  assert.equal(classifyExampleAction("   \n\t", example), "fill");
});

test("JSON-equivalent payload classifies as matching, ignoring formatting and key order", () => {
  assert.equal(classifyExampleAction(example, example), "matching");
  assert.equal(
    classifyExampleAction('{\n  "count": 3,\n  "name": "Ada"\n}', example),
    "matching",
  );
});

test("different payload classifies as reset", () => {
  assert.equal(classifyExampleAction('{"name":"Grace","count":3}', example), "reset");
  assert.equal(classifyExampleAction('{"name":"Ada"}', example), "reset");
});

test("non-blank unparseable payload classifies as reset (never discarded silently)", () => {
  assert.equal(classifyExampleAction("{ not json", example), "reset");
  assert.equal(classifyExampleAction("just some text", example), "reset");
});

test("unparseable example is not a match, so a non-blank payload resets", () => {
  assert.equal(classifyExampleAction('{"name":"Ada"}', "{ broken"), "reset");
  // A blank payload is still a fill regardless of the example text.
  assert.equal(classifyExampleAction("", "{ broken"), "fill");
});
