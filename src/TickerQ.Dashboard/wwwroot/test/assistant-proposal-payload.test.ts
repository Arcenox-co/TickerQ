import { test } from "node:test";
import assert from "node:assert/strict";

import { proposalChainToTimeTickerNode } from "../src/lib/assistant-proposal-payload.ts";

const utf8Base64 = (value: string) => Buffer.from(value, "utf-8").toString("base64");

test("assistant chain mapping Base64-encodes Unicode payloads recursively", () => {
  const rootJson = '{"message":"héllo 世界 🚀"}';
  const childJson = '{"emoji":"✅"}';

  const mapped = proposalChainToTimeTickerNode(
    {
      function: "RootJob",
      requestJson: rootJson,
      children: [
        {
          function: "ChildJob",
          requestJson: childJson,
          runCondition: "OnFailure",
          children: [{ function: "LeafJob", requestJson: "" }],
        },
      ],
    },
    true,
  );

  assert.equal(mapped.request, utf8Base64(rootJson));
  assert.equal(mapped.runCondition, null);
  assert.equal(mapped.children[0].request, utf8Base64(childJson));
  assert.equal(mapped.children[0].runCondition, "OnFailure");
  assert.equal(mapped.children[0].children[0].request, null);
});
