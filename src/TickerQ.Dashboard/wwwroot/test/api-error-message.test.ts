import { test } from "node:test";
import assert from "node:assert/strict";

import { formatApiErrorMessage } from "../src/lib/api-error-message.ts";

// Backend errors arrive as JSON. Rendering the raw body leaked braces/quotes
// into the UI as `400 Bad Request — {"error":"..."}`. These tests pin the
// field-extraction priority and the `STATUS statusText — message` shape.

test("extracts `error` from a JSON object body", () => {
  assert.equal(
    formatApiErrorMessage(400, "Bad Request", '{"error":"Ticker not found"}'),
    "400 Bad Request — Ticker not found",
  );
});

test("falls back to `detail` when `error` is absent", () => {
  assert.equal(
    formatApiErrorMessage(422, "Unprocessable Entity", '{"detail":"Cron expression invalid"}'),
    "422 Unprocessable Entity — Cron expression invalid",
  );
});

test("falls back to `title` when `error` and `detail` are absent", () => {
  assert.equal(
    formatApiErrorMessage(409, "Conflict", '{"title":"Already running"}'),
    "409 Conflict — Already running",
  );
});

test("prefers `error` over `detail` and `title`", () => {
  assert.equal(
    formatApiErrorMessage(
      400,
      "Bad Request",
      '{"error":"primary","detail":"secondary","title":"tertiary"}',
    ),
    "400 Bad Request — primary",
  );
});

test("falls back to plain text when the body is not JSON", () => {
  assert.equal(
    formatApiErrorMessage(500, "Internal Server Error", "boom"),
    "500 Internal Server Error — boom",
  );
});

test("omits the separator when the body is empty", () => {
  assert.equal(formatApiErrorMessage(503, "Service Unavailable", ""), "503 Service Unavailable");
});

test("ignores non-string / blank fields and falls through", () => {
  // `error` is present but not a usable string -> fall through to `detail`.
  assert.equal(
    formatApiErrorMessage(400, "Bad Request", '{"error":123,"detail":"real message"}'),
    "400 Bad Request — real message",
  );
  // No usable field at all -> keep the raw JSON text rather than dropping it.
  assert.equal(
    formatApiErrorMessage(400, "Bad Request", '{"error":"   "}'),
    '400 Bad Request — {"error":"   "}',
  );
});
