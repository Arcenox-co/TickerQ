// Pure JSON Schema classification for the request-payload editor.
//
// The dashboard renders one level of fields for a function's request contract.
// The contract is a JSON Schema 2020-12 document emitted by JsonSchemaEmitter:
// object-rooted, nested objects carried as local `$ref` into `$defs`, nullable
// objects as `anyOf: [ref, {type:"null"}]`, dictionaries as an
// `additionalProperties` sub-schema, numeric enums as plain integers.
//
// This module is intentionally framework-free so it can be unit-tested with
// node:test. It resolves local `$ref` pointers (with a cycle guard), unwraps the
// supported nullable union shape, and classifies each top-level property into a
// concrete editor control. Anything it cannot model safely is reported as
// `unsupported`, which the editor turns into a raw-JSON fallback rather than a
// misleading control — the wire text is always preserved.

/** A single editor control a property can map to. */
export type FieldControl =
  | "string"
  | "number"
  | "integer"
  | "boolean"
  | "enum"
  | "object"
  | "array"
  | "dictionary";

/** Result of classifying one schema node. */
export type FieldClassification =
  | { control: FieldControl; enumValues?: unknown[] }
  | { control: "unsupported"; reason: string };

/** A classified top-level property, ready to render. */
export interface ClassifiedField {
  name: string;
  control: FieldControl;
  required: boolean;
  enumValues?: unknown[];
  title?: string;
  description?: string;
}

/** The editor either renders fields or falls back to a raw-JSON textarea. */
export type SchemaModel =
  | { supported: false }
  | { supported: true; fields: ClassifiedField[] };

type SchemaNode = Record<string, unknown>;

/** Hard budget that guarantees recursive request schemas cannot mount forever. */
export const MAX_SCHEMA_RENDER_DEPTH = 8;

export function exceedsSchemaRenderDepth(depth: number): boolean {
  return depth >= MAX_SCHEMA_RENDER_DEPTH;
}

export function removeInvalidArrayIndex(invalid: ReadonlySet<number>, removedIndex: number): Set<number> {
  const next = new Set<number>();
  for (const index of invalid) {
    if (index < removedIndex) next.add(index);
    else if (index > removedIndex) next.add(index - 1);
  }
  return next;
}

export function renameInvalidDictionaryKey(
  invalid: ReadonlySet<string>,
  from: string,
  to: string,
): Set<string> {
  const next = new Set(invalid);
  if (next.delete(from)) next.add(to);
  return next;
}

function isObject(value: unknown): value is SchemaNode {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Resolve a local JSON Pointer (RFC 6901), e.g. `#/$defs/Address` or
 * `#/definitions/Node`, against `root`. Returns `undefined` for non-local URIs
 * (anything not starting with `#`) or unresolvable paths.
 */
export function resolveLocalPointer(root: unknown, pointer: string): unknown {
  if (typeof pointer !== "string" || !pointer.startsWith("#")) return undefined;
  const path = pointer.slice(1);
  if (path === "" || path === "/") return root;
  if (!path.startsWith("/")) return undefined;

  let current: unknown = root;
  for (const rawSegment of path.slice(1).split("/")) {
    const segment = rawSegment.replace(/~1/g, "/").replace(/~0/g, "~");
    if (!isObject(current) || !(segment in current)) return undefined;
    current = current[segment];
  }
  return current;
}

/** True for a schema node that only permits JSON null. */
function isNullSchema(node: unknown): boolean {
  if (!isObject(node)) return false;
  const type = node.type;
  if (type === "null") return true;
  return Array.isArray(type) && type.length > 0 && type.every((entry) => entry === "null");
}

/**
 * Peel the supported nullable union shape. `anyOf`/`oneOf` of exactly one
 * non-null branch plus any number of null branches unwraps to that branch and
 * flags `nullable`. A plain node passes through unchanged. Any other union
 * (two real branches, empty, malformed) is reported unsupported.
 */
export function unwrapNullableUnion(
  node: unknown,
):
  | { supported: true; schema: SchemaNode; nullable: boolean }
  | { supported: false } {
  if (!isObject(node)) return { supported: false };

  const union = node.anyOf ?? node.oneOf;
  if (union === undefined) return { supported: true, schema: node, nullable: false };
  if (!Array.isArray(union)) return { supported: false };

  const nonNull = union.filter((branch) => !isNullSchema(branch));
  if (nonNull.length !== 1 || !isObject(nonNull[0])) return { supported: false };

  const nullable = nonNull.length !== union.length;
  return { supported: true, schema: nonNull[0], nullable };
}

const MAX_RESOLVE_HOPS = 64;

/**
 * Normalize a node to a concrete schema by alternately resolving local `$ref`
 * pointers and unwrapping nullable unions. The `seen` set of visited pointers is
 * the cycle guard: a pointer encountered twice is a degenerate ref loop with no
 * concrete body and is reported unsupported.
 */
function normalize(root: unknown, node: unknown): SchemaNode | null {
  const seen = new Set<string>();
  let current = node;
  for (let hop = 0; hop < MAX_RESOLVE_HOPS; hop++) {
    if (!isObject(current)) return null;

    if (typeof current.$ref === "string") {
      const pointer = current.$ref;
      if (!pointer.startsWith("#") || seen.has(pointer)) return null;
      seen.add(pointer);
      const resolved = resolveLocalPointer(root, pointer);
      if (resolved === undefined) return null;
      current = resolved;
      continue;
    }

    if (current.anyOf !== undefined || current.oneOf !== undefined) {
      const unwrapped = unwrapNullableUnion(current);
      if (!unwrapped.supported) return null;
      current = unwrapped.schema;
      continue;
    }

    return current;
  }
  return null;
}

/** The JSON type keyword, with any `"null"` member stripped. */
function primaryType(node: SchemaNode): string | undefined {
  const type = node.type;
  if (typeof type === "string") return type;
  if (Array.isArray(type)) {
    const real = type.find((entry) => entry !== "null");
    return typeof real === "string" ? real : undefined;
  }
  return undefined;
}

/**
 * Classify one schema node into an editor control, resolving refs/unions first.
 * Structured shapes (object, array, dictionary) are distinct controls so the
 * editor can give each a JSON textarea; primitives and enums get inline inputs.
 */
export function classifyField(root: unknown, node: unknown): FieldClassification {
  const normalized = normalize(root, node);
  if (normalized === null) return { control: "unsupported", reason: "unresolvable ref or union" };

  if (Array.isArray(normalized.enum)) {
    return { control: "enum", enumValues: normalized.enum };
  }

  switch (primaryType(normalized)) {
    case "boolean":
      return { control: "boolean" };
    case "integer":
      return { control: "integer" };
    case "number":
      return { control: "number" };
    case "string":
      return { control: "string" };
    case "array":
      return { control: "array" };
    case "object":
      if (isObject(normalized.properties)) return { control: "object" };
      if (isObject(normalized.additionalProperties)) return { control: "dictionary" };
      return { control: "object" };
    default:
      return { control: "unsupported", reason: "no inferable control" };
  }
}

/**
 * Build the editor model from a contract's `schemaJson`. Returns field controls
 * only when the schema is an object-rooted document whose every top-level
 * property classifies to a supported control; otherwise the editor renders raw
 * JSON so no shape is ever mis-edited and no text is lost.
 */
export function buildRequestFields(schemaJson: string | null | undefined): SchemaModel {
  if (!schemaJson) return { supported: false };

  let root: unknown;
  try {
    root = JSON.parse(schemaJson);
  } catch {
    return { supported: false };
  }

  if (!isObject(root) || !isObject(root.properties)) return { supported: false };

  const required = new Set(Array.isArray(root.required) ? root.required : []);
  const fields: ClassifiedField[] = [];

  for (const [name, property] of Object.entries(root.properties)) {
    const classified = classifyField(root, property);
    if (classified.control === "unsupported") return { supported: false };

    const field: ClassifiedField = {
      name,
      control: classified.control,
      required: required.has(name),
    };
    if (classified.enumValues) field.enumValues = classified.enumValues;
    if (isObject(property)) {
      if (typeof property.title === "string") field.title = property.title;
      if (typeof property.description === "string") field.description = property.description;
    }
    fields.push(field);
  }

  return { supported: true, fields };
}

// ---- recursive node resolution ----------------------------------------------
//
// The flat `buildRequestFields` above powers the one-level summary. The payload
// dialog instead renders a recursive tree, so it needs to resolve any node —
// including array item schemas and nested object properties — on demand. These
// helpers stay pure/framework-free so the dialog can descend lazily (which also
// makes self-referential schemas safe: a branch is only resolved when expanded).

/** One property of a resolved object node, ready to render/recurse into. */
export interface ResolvedProperty {
  name: string;
  schema: SchemaNode;
  required: boolean;
  title?: string;
  description?: string;
}

/**
 * A schema node resolved to a concrete editor shape. `object`, `array` and
 * `dictionary` carry the child schema(s) so the caller can recurse; primitives
 * and enums are leaves; `unsupported` means "render a raw-JSON box for just this
 * subtree" rather than failing the whole payload.
 */
export type ResolvedNode =
  | { kind: "object"; properties: ResolvedProperty[] }
  | { kind: "array"; items: SchemaNode | null }
  | { kind: "dictionary"; values: SchemaNode | null }
  | { kind: "enum"; enumValues: unknown[] }
  | { kind: "string" | "number" | "integer" | "boolean" }
  | { kind: "unsupported"; reason: string };

function readString(node: SchemaNode, key: string): string | undefined {
  const value = node[key];
  return typeof value === "string" ? value : undefined;
}

/**
 * Resolve a single node (after refs/nullable unwrapping) into a `ResolvedNode`.
 * Unlike `classifyField`, this exposes children so a recursive editor can walk
 * the whole document. Cycles are naturally bounded because callers resolve one
 * level at a time; the returned child `schema` values are left unresolved.
 */
export function resolveSchemaNode(root: unknown, node: unknown): ResolvedNode {
  const normalized = normalize(root, node);
  if (normalized === null) return { kind: "unsupported", reason: "unresolvable ref or union" };

  if (Array.isArray(normalized.enum)) return { kind: "enum", enumValues: normalized.enum };

  switch (primaryType(normalized)) {
    case "boolean":
      return { kind: "boolean" };
    case "integer":
      return { kind: "integer" };
    case "number":
      return { kind: "number" };
    case "string":
      return { kind: "string" };
    case "array": {
      // 2020-12 tuples (`prefixItems`, or `items` as an array) are not modelled.
      if (normalized.prefixItems !== undefined) return { kind: "unsupported", reason: "tuple items" };
      const items = normalized.items;
      if (Array.isArray(items)) return { kind: "unsupported", reason: "tuple items" };
      return { kind: "array", items: isObject(items) ? items : null };
    }
    case "object": {
      if (isObject(normalized.properties)) {
        const required = new Set(Array.isArray(normalized.required) ? normalized.required : []);
        const properties: ResolvedProperty[] = Object.entries(normalized.properties).map(
          ([name, schema]) => ({
            name,
            schema: (isObject(schema) ? schema : {}) as SchemaNode,
            required: required.has(name),
            title: isObject(schema) ? readString(schema, "title") : undefined,
            description: isObject(schema) ? readString(schema, "description") : undefined,
          }),
        );
        return { kind: "object", properties };
      }
      if (isObject(normalized.additionalProperties)) {
        return { kind: "dictionary", values: normalized.additionalProperties };
      }
      if (normalized.additionalProperties === false) {
        // Sealed object with no declared properties: nothing to edit structurally.
        return { kind: "unsupported", reason: "object without properties" };
      }
      return { kind: "dictionary", values: null };
    }
    default:
      return { kind: "unsupported", reason: "no inferable control" };
  }
}

/**
 * Resolve the contract's root schema, but only when it is an object with
 * declared properties — the shape the structured tree can edit. Anything else
 * (array root, primitive root, unparseable) returns null so the dialog offers
 * the raw-JSON editor alone.
 */
export function resolveRootObject(
  schemaJson: string | null | undefined,
): { root: unknown; properties: ResolvedProperty[] } | null {
  if (!schemaJson) return null;
  let root: unknown;
  try {
    root = JSON.parse(schemaJson);
  } catch {
    return null;
  }
  const resolved = resolveSchemaNode(root, root);
  if (resolved.kind !== "object") return null;
  return { root, properties: resolved.properties };
}

/**
 * A blank value that satisfies a node's declared type, used when adding an array
 * item or dictionary entry. Objects/arrays start empty; primitives start at a
 * neutral value; unresolvable nodes start as null so the raw box shows `null`.
 */
export function emptyValueForNode(root: unknown, node: unknown): unknown {
  const resolved = resolveSchemaNode(root, node);
  switch (resolved.kind) {
    case "object":
      return {};
    case "array":
      return [];
    case "dictionary":
      return {};
    case "boolean":
      return false;
    case "integer":
    case "number":
      return 0;
    case "string":
      return "";
    case "enum":
      return resolved.enumValues[0];
    default:
      return null;
  }
}

// ---- structured field draft --------------------------------------------------

/**
 * Outcome of parsing a structured field's textarea. `empty` clears the field;
 * `valid` publishes the parsed value to the parent payload; `invalid` keeps the
 * raw text so an in-progress `{` or `[` is never discarded.
 */
export type StructuredParse =
  | { status: "empty" }
  | { status: "valid"; value: unknown }
  | { status: "invalid"; error: string };

export function parseStructuredInput(text: string): StructuredParse {
  if (!text.trim()) return { status: "empty" };
  try {
    return { status: "valid", value: JSON.parse(text) };
  } catch {
    return { status: "invalid", error: "Enter valid JSON." };
  }
}

export function isRawPayloadJsonValid(text: string): boolean {
  if (!text.trim()) return true;
  try {
    JSON.parse(text);
    return true;
  } catch {
    return false;
  }
}

/** Pretty-print a value for a structured field; null/undefined show as blank. */
export function formatStructuredValue(value: unknown): string {
  if (value === undefined || value === null) return "";
  return JSON.stringify(value, null, 2);
}

/** Deterministic, key-sorted stringify so value equality ignores key order. */
function canonicalJson(value: unknown): string {
  if (Array.isArray(value)) return "[" + value.map(canonicalJson).join(",") + "]";
  if (isObject(value)) {
    return (
      "{" +
      Object.keys(value)
        .sort()
        .map((key) => JSON.stringify(key) + ":" + canonicalJson(value[key]))
        .join(",") +
      "}"
    );
  }
  return JSON.stringify(value ?? null);
}

/** Structural JSON equality, insensitive to object key order. */
export function sameJsonValue(a: unknown, b: unknown): boolean {
  return canonicalJson(a) === canonicalJson(b);
}

/** What loading a generated example would do to the current payload. */
export type ExampleAction = "fill" | "matching" | "reset";

/**
 * Classify the single-example action against the current payload text:
 * `fill` when the payload is blank (nothing to lose), `matching` when it is
 * already JSON-equivalent to the example (a no-op), and `reset` when it holds
 * different — or non-blank-but-unparseable — data that an overwrite would
 * clobber. Unparseable text is never treated as a match, so it is never
 * discarded without an explicit, confirmed reset.
 */
export function classifyExampleAction(
  currentValue: string,
  exampleValueJson: string,
): ExampleAction {
  if (!currentValue.trim()) return "fill";
  let current: unknown;
  try {
    current = JSON.parse(currentValue);
  } catch {
    return "reset";
  }
  try {
    return sameJsonValue(current, JSON.parse(exampleValueJson)) ? "matching" : "reset";
  } catch {
    return "reset";
  }
}

/** Live textarea state for a structured field. */
export interface StructuredDraft {
  text: string;
  error: string | null;
}

export function initStructuredDraft(value: unknown): StructuredDraft {
  return { text: formatStructuredValue(value), error: null };
}

/**
 * Reconcile a draft against the parent value after a re-render. When the draft
 * still represents the parent value (or both are empty) the exact same draft
 * object is returned so the user's in-progress text survives — including
 * un-parseable intermediates. Only a genuine external change (e.g. loading an
 * example) resets the draft to the new value.
 */
export function syncStructuredDraft(
  draft: StructuredDraft,
  previousValue: unknown,
  nextValue: unknown,
): StructuredDraft {
  if (sameJsonValue(previousValue, nextValue)) return draft;
  return initStructuredDraft(nextValue);
}
