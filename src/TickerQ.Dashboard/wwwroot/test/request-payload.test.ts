import { test } from "node:test";
import assert from "node:assert/strict";

import {
  decodeRequestPayload,
  encodeRequestPayload,
} from "../src/lib/request-payload.ts";

// The server DTO models `request` as byte[]; JSON transport therefore carries it
// as base64. These tests pin the exact wire bytes so the round trip cannot drift.

test("encodes ASCII JSON as UTF-8 base64", () => {
  const json = '{"name":"alice","count":3}';
  const encoded = encodeRequestPayload(json);
  assert.equal(encoded, Buffer.from(json, "utf-8").toString("base64"));
  assert.equal(decodeRequestPayload(encoded), json);
});

test("round-trips Unicode (multi-byte) without corruption", () => {
  const json = '{"greeting":"héllo 世界 🚀","emoji":"✅"}';
  const encoded = encodeRequestPayload(json);
  // Must be valid base64 of the UTF-8 bytes, not the raw string.
  assert.equal(encoded, Buffer.from(json, "utf-8").toString("base64"));
  assert.equal(decodeRequestPayload(encoded), json);
});

test("empty / requestless payloads map to null", () => {
  assert.equal(encodeRequestPayload(""), null);
  assert.equal(encodeRequestPayload(null), null);
  assert.equal(encodeRequestPayload(undefined), null);
});

test("decode returns empty string for null/empty base64", () => {
  assert.equal(decodeRequestPayload(null), "");
  assert.equal(decodeRequestPayload(undefined), "");
  assert.equal(decodeRequestPayload(""), "");
});

test("decode falls back to raw text for legacy non-base64 payloads", () => {
  // Older rows stored the JSON verbatim; `{` is not valid base64 so atob throws.
  const legacy = '{"legacy":true}';
  assert.equal(decodeRequestPayload(legacy), legacy);
});

test("nested chain node mapping encodes every node, requestless stays null", () => {
  type Node = { requestJson: string; children: Node[] };
  const tree: Node = {
    requestJson: '{"step":"root"}',
    children: [
      { requestJson: '{"step":"child-ünïcode 🌟"}', children: [] },
      {
        requestJson: "",
        children: [{ requestJson: '{"leaf":1}', children: [] }],
      },
    ],
  };

  const mapRequest = (node: Node): {
    request: string | null;
    children: ReturnType<typeof mapRequest>[];
  } => ({
    request: encodeRequestPayload(node.requestJson),
    children: node.children.map(mapRequest),
  });

  const mapped = mapRequest(tree);

  assert.equal(mapped.request, encodeRequestPayload('{"step":"root"}'));
  assert.equal(
    mapped.children[0].request,
    Buffer.from('{"step":"child-ünïcode 🌟"}', "utf-8").toString("base64"),
  );
  // Requestless node preserved as null (not an empty base64 blob).
  assert.equal(mapped.children[1].request, null);
  assert.equal(mapped.children[1].children[0].request, encodeRequestPayload('{"leaf":1}'));

  // Every non-null node decodes back to its original JSON text.
  assert.equal(decodeRequestPayload(mapped.request), '{"step":"root"}');
  assert.equal(decodeRequestPayload(mapped.children[0].request), '{"step":"child-ünïcode 🌟"}');
});
