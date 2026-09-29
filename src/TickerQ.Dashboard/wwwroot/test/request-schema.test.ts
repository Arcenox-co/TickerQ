import { test } from "node:test";
import assert from "node:assert/strict";

import {
  buildRequestFields,
  classifyField,
  emptyValueForNode,
  exceedsSchemaRenderDepth,
  formatStructuredValue,
  initStructuredDraft,
  isRawPayloadJsonValid,
  nextAvailableRowIdentity,
  parseStructuredInput,
  pruneInvalidRowIdentities,
  removeArrayRowIdentity,
  renameDictionaryRowIdentity,
  resolveLocalPointer,
  resolveRootObject,
  resolveSchemaNode,
  sameJsonValue,
  syncStructuredDraft,
  unwrapNullableUnion,
  MAX_SCHEMA_RENDER_DEPTH,
} from "../src/lib/request-schema.ts";

test("recursive schema rendering has a finite raw-JSON fallback budget", () => {
  assert.equal(exceedsSchemaRenderDepth(MAX_SCHEMA_RENDER_DEPTH - 1), false);
  assert.equal(exceedsSchemaRenderDepth(MAX_SCHEMA_RENDER_DEPTH), true);
  assert.equal(exceedsSchemaRenderDepth(MAX_SCHEMA_RENDER_DEPTH + 100), true);
});

test("raw payload validity recovers after replacing invalid structured text", () => {
  assert.equal(isRawPayloadJsonValid("{"), false);
  assert.equal(isRawPayloadJsonValid('{"Name":"Ada"}'), true);
  assert.equal(isRawPayloadJsonValid(""), true);
});

test("array removal preserves the React identity of every surviving draft", () => {
  const identities = ["first-draft", "invalid-draft", "last-draft"];
  assert.deepEqual(removeArrayRowIdentity(identities, 0), ["invalid-draft", "last-draft"]);
});

test("dictionary rename preserves the React identity of the renamed draft", () => {
  const identities = new Map([
    ["old", "invalid-draft"],
    ["other", "other-draft"],
  ]);
  assert.deepEqual(
    [...renameDictionaryRowIdentity(identities, "old", "new")],
    [
      ["other", "other-draft"],
      ["new", "invalid-draft"],
    ],
  );
});

test("removed row identities are pruned without clearing surviving invalid drafts", () => {
  assert.deepEqual(
    [...pruneInvalidRowIdentities(new Set(["removed", "surviving"]), ["surviving", "valid"])],
    ["surviving"],
  );
});

test("array identities remain unique after external growth, removal, and regrowth", () => {
  let identities = ["row-0"];
  while (identities.length < 3) {
    identities.push(nextAvailableRowIdentity(`external-${identities.length}`, identities));
  }
  identities = removeArrayRowIdentity(identities, 1);
  identities.push(nextAvailableRowIdentity(`external-${identities.length}`, identities));
  assert.deepEqual(identities, ["row-0", "external-2", "external-2-next"]);
  assert.equal(new Set(identities).size, identities.length);
});

test("dictionary identities remain unique after rename and external key reintroduction", () => {
  let identities = new Map([["foo", "external-foo"]]);
  identities = renameDictionaryRowIdentity(identities, "foo", "bar");
  identities.set("foo", nextAvailableRowIdentity("external-foo", identities.values()));
  assert.deepEqual([...identities.values()], ["external-foo", "external-foo-next"]);
  assert.equal(new Set(identities.values()).size, identities.size);
});

// A JsonSchemaEmitter-shaped document: object-rooted, PascalCase props, numeric
// enums as plain integers, nested objects as local $ref into $defs, nullable
// objects as anyOf[ref, null], dictionaries as additionalProperties schemas.
function emitterSchema(): Record<string, unknown> {
  return {
    $schema: "https://json-schema.org/draft/2020-12/schema",
    type: "object",
    properties: {
      Name: { type: "string" },
      Count: { type: "integer", minimum: -2147483648, maximum: 2147483647 },
      Ratio: { type: "number" },
      Active: { type: "boolean" },
      Nickname: { type: ["string", "null"] },
      Address: { $ref: "#/$defs/Address" },
      Billing: { anyOf: [{ $ref: "#/$defs/Address" }, { type: "null" }] },
      Tags: { type: "array", items: { type: "string" } },
      Contacts: { type: "array", items: { $ref: "#/$defs/Address" } },
      Metadata: { type: "object", additionalProperties: { type: "string" } },
    },
    required: ["Name"],
    additionalProperties: false,
    $defs: {
      Address: {
        type: "object",
        properties: { City: { type: "string" } },
        additionalProperties: false,
      },
    },
  };
}

// ---- resolveLocalPointer -------------------------------------------------

test("resolveLocalPointer follows a local $defs pointer to its body", () => {
  const root = emitterSchema();
  const resolved = resolveLocalPointer(root, "#/$defs/Address");
  assert.deepEqual(resolved, {
    type: "object",
    properties: { City: { type: "string" } },
    additionalProperties: false,
  });
});

test("resolveLocalPointer also handles the /definitions dialect", () => {
  const root = { definitions: { Node: { type: "object", properties: {} } } };
  assert.deepEqual(resolveLocalPointer(root, "#/definitions/Node"), {
    type: "object",
    properties: {},
  });
});

test("resolveLocalPointer decodes ~1 and ~0 escapes (RFC 6901)", () => {
  const root = { $defs: { "a/b~c": { type: "string" } } };
  assert.deepEqual(resolveLocalPointer(root, "#/$defs/a~1b~0c"), { type: "string" });
});

test("resolveLocalPointer returns undefined for missing or non-local pointers", () => {
  const root = emitterSchema();
  assert.equal(resolveLocalPointer(root, "#/$defs/Missing"), undefined);
  assert.equal(resolveLocalPointer(root, "https://example.com/schema"), undefined);
});

// ---- unwrapNullableUnion --------------------------------------------------

test("unwrapNullableUnion peels a [ref, null] anyOf to the single schema", () => {
  const result = unwrapNullableUnion({
    anyOf: [{ $ref: "#/$defs/Address" }, { type: "null" }],
  });
  assert.equal(result.supported, true);
  assert.equal(result.nullable, true);
  assert.deepEqual(result.schema, { $ref: "#/$defs/Address" });
});

test("unwrapNullableUnion accepts oneOf and passes through non-unions", () => {
  const one = unwrapNullableUnion({ oneOf: [{ type: "string" }, { type: "null" }] });
  assert.equal(one.supported, true);
  assert.deepEqual(one.schema, { type: "string" });

  const plain = unwrapNullableUnion({ type: "string" });
  assert.equal(plain.supported, true);
  assert.equal(plain.nullable, false);
  assert.deepEqual(plain.schema, { type: "string" });
});

test("unwrapNullableUnion rejects unions with more than one non-null branch", () => {
  const result = unwrapNullableUnion({
    anyOf: [{ type: "string" }, { type: "integer" }],
  });
  assert.equal(result.supported, false);
});

// ---- classifyField --------------------------------------------------------

test("classifyField maps every emitter-shaped property to the right control", () => {
  const root = emitterSchema();
  const props = root.properties as Record<string, unknown>;
  const control = (name: string) => classifyField(root, props[name]);

  assert.deepEqual(control("Name"), { control: "string" });
  assert.deepEqual(control("Count"), { control: "integer" });
  assert.deepEqual(control("Ratio"), { control: "number" });
  assert.deepEqual(control("Active"), { control: "boolean" });
  assert.deepEqual(control("Nickname"), { control: "string" });
  assert.deepEqual(control("Address"), { control: "object" });
  assert.deepEqual(control("Billing"), { control: "object" });
  assert.deepEqual(control("Tags"), { control: "array" });
  assert.deepEqual(control("Contacts"), { control: "array" });
  assert.deepEqual(control("Metadata"), { control: "dictionary" });
});

test("classifyField reports enum with its allowed values", () => {
  const root = { properties: {} };
  assert.deepEqual(classifyField(root, { enum: ["A", "B"] }), {
    control: "enum",
    enumValues: ["A", "B"],
  });
});

test("classifyField resolves a self-referential object as object (no false cycle)", () => {
  const root = {
    $defs: {
      Node: {
        type: "object",
        properties: { Parent: { $ref: "#/$defs/Node" } },
        additionalProperties: false,
      },
    },
  };
  assert.deepEqual(classifyField(root, { $ref: "#/$defs/Node" }), { control: "object" });
});

test("classifyField falls back to unsupported for a degenerate ref cycle", () => {
  const root = {
    $defs: {
      A: { $ref: "#/$defs/B" },
      B: { $ref: "#/$defs/A" },
    },
  };
  const result = classifyField(root, { $ref: "#/$defs/A" });
  assert.equal(result.control, "unsupported");
});

test("classifyField falls back to unsupported for unresolvable refs", () => {
  const result = classifyField({ $defs: {} }, { $ref: "#/$defs/Gone" });
  assert.equal(result.control, "unsupported");
});

test("classifyField falls back to unsupported for multi-branch unions and shapeless nodes", () => {
  assert.equal(
    classifyField({}, { anyOf: [{ type: "string" }, { type: "integer" }] }).control,
    "unsupported",
  );
  assert.equal(classifyField({}, {}).control, "unsupported");
});

// ---- buildRequestFields ---------------------------------------------------

test("buildRequestFields classifies a full emitter schema into ordered fields", () => {
  const model = buildRequestFields(JSON.stringify(emitterSchema()));
  assert.equal(model.supported, true);
  if (!model.supported) return;

  const byName = Object.fromEntries(model.fields.map((f) => [f.name, f]));
  assert.equal(byName.Name.control, "string");
  assert.equal(byName.Name.required, true);
  assert.equal(byName.Count.required, false);
  assert.equal(byName.Address.control, "object");
  assert.equal(byName.Billing.control, "object");
  assert.equal(byName.Metadata.control, "dictionary");
  assert.equal(model.fields.length, 10);
});

test("buildRequestFields falls back to raw when any field is unsupported", () => {
  const schema = emitterSchema();
  (schema.properties as Record<string, unknown>).Weird = {
    anyOf: [{ type: "string" }, { type: "integer" }],
  };
  assert.equal(buildRequestFields(JSON.stringify(schema)).supported, false);
});

test("buildRequestFields falls back to raw for null / invalid / non-object schemas", () => {
  assert.equal(buildRequestFields(null).supported, false);
  assert.equal(buildRequestFields("not json").supported, false);
  assert.equal(buildRequestFields(JSON.stringify({ type: "array" })).supported, false);
});

// ---- resolveSchemaNode (recursive) ----------------------------------------

test("resolveSchemaNode exposes object properties with required flags", () => {
  const root = emitterSchema();
  const resolved = resolveSchemaNode(root, root);
  assert.equal(resolved.kind, "object");
  if (resolved.kind !== "object") return;
  const byName = Object.fromEntries(resolved.properties.map((p) => [p.name, p]));
  assert.equal(byName.Name.required, true);
  assert.equal(byName.Count.required, false);
  assert.equal(byName.Address.required, false);
});

test("resolveSchemaNode returns the array item schema for arrays of objects", () => {
  const root = emitterSchema();
  const props = root.properties as Record<string, unknown>;
  const resolved = resolveSchemaNode(root, props.Contacts);
  assert.equal(resolved.kind, "array");
  if (resolved.kind !== "array") return;
  // Item schema is left unresolved so callers descend lazily.
  assert.deepEqual(resolved.items, { $ref: "#/$defs/Address" });
});

test("resolveSchemaNode returns the primitive item schema for arrays of primitives", () => {
  const resolved = resolveSchemaNode({}, { type: "array", items: { type: "string" } });
  assert.equal(resolved.kind, "array");
  if (resolved.kind !== "array") return;
  assert.deepEqual(resolved.items, { type: "string" });
});

test("resolveSchemaNode returns the dictionary value schema", () => {
  const root = emitterSchema();
  const props = root.properties as Record<string, unknown>;
  const resolved = resolveSchemaNode(root, props.Metadata);
  assert.equal(resolved.kind, "dictionary");
  if (resolved.kind !== "dictionary") return;
  assert.deepEqual(resolved.values, { type: "string" });
});

test("resolveSchemaNode unwraps a nullable object ref into an object node", () => {
  const root = emitterSchema();
  const props = root.properties as Record<string, unknown>;
  const resolved = resolveSchemaNode(root, props.Billing);
  assert.equal(resolved.kind, "object");
});

test("resolveSchemaNode reports tuples and multi-branch unions as unsupported", () => {
  assert.equal(resolveSchemaNode({}, { type: "array", prefixItems: [{ type: "string" }] }).kind, "unsupported");
  assert.equal(resolveSchemaNode({}, { type: "array", items: [{ type: "string" }] }).kind, "unsupported");
  assert.equal(
    resolveSchemaNode({}, { anyOf: [{ type: "string" }, { type: "integer" }] }).kind,
    "unsupported",
  );
});

test("resolveSchemaNode treats a sealed object without properties as unsupported", () => {
  assert.equal(resolveSchemaNode({}, { type: "object", additionalProperties: false }).kind, "unsupported");
});

// ---- resolveRootObject -----------------------------------------------------

test("resolveRootObject exposes top-level properties for an object schema", () => {
  const result = resolveRootObject(JSON.stringify(emitterSchema()));
  assert.notEqual(result, null);
  assert.equal(result?.properties.length, 10);
});

test("resolveRootObject returns null for non-object / invalid roots", () => {
  assert.equal(resolveRootObject(JSON.stringify({ type: "array", items: { type: "string" } })), null);
  assert.equal(resolveRootObject("not json"), null);
  assert.equal(resolveRootObject(null), null);
});

// ---- emptyValueForNode -----------------------------------------------------

test("emptyValueForNode produces a neutral value per node kind", () => {
  assert.deepEqual(emptyValueForNode({}, { type: "object", properties: {} }), {});
  assert.deepEqual(emptyValueForNode({}, { type: "array", items: { type: "string" } }), []);
  assert.equal(emptyValueForNode({}, { type: "string" }), "");
  assert.equal(emptyValueForNode({}, { type: "integer" }), 0);
  assert.equal(emptyValueForNode({}, { type: "boolean" }), false);
  assert.equal(emptyValueForNode({}, { enum: ["A", "B"] }), "A");
  assert.equal(emptyValueForNode({}, { anyOf: [{ type: "string" }, { type: "integer" }] }), null);
});

// ---- structured draft helpers ---------------------------------------------

test("parseStructuredInput distinguishes empty, valid and invalid JSON", () => {
  assert.deepEqual(parseStructuredInput("   "), { status: "empty" });
  assert.deepEqual(parseStructuredInput('{"a":1}'), { status: "valid", value: { a: 1 } });
  assert.equal(parseStructuredInput("{").status, "invalid");
});

test("formatStructuredValue pretty-prints values and blanks null/undefined", () => {
  assert.equal(formatStructuredValue({ a: 1 }), '{\n  "a": 1\n}');
  assert.equal(formatStructuredValue(undefined), "");
  assert.equal(formatStructuredValue(null), "");
});

test("sameJsonValue ignores object key order", () => {
  assert.equal(sameJsonValue({ a: 1, b: 2 }, { b: 2, a: 1 }), true);
  assert.equal(sameJsonValue({ a: 1 }, { a: 2 }), false);
});

test("syncStructuredDraft retains an in-progress '{' while the parent is unchanged", () => {
  // User typed a lone "{" which does not parse; the parent value is still the
  // last valid object. The draft text must survive the re-render.
  const draft = { text: "{", error: "Enter valid JSON." };
  const next = syncStructuredDraft(draft, { city: "Berlin" }, { city: "Berlin" });
  assert.equal(next.text, "{");
  assert.equal(next.error, "Enter valid JSON.");
});

test("syncStructuredDraft keeps the draft when it still represents the parent value", () => {
  const draft = initStructuredDraft({ a: 1 });
  const next = syncStructuredDraft(draft, { a: 1 }, { a: 1 });
  assert.equal(next, draft);
});

test("syncStructuredDraft resets when the parent value changes externally", () => {
  const draft = { text: "{", error: "Enter valid JSON." };
  const next = syncStructuredDraft(draft, { a: 1 }, { a: 2 });
  assert.equal(next.error, null);
  assert.deepEqual(parseStructuredInput(next.text), { status: "valid", value: { a: 2 } });
});
