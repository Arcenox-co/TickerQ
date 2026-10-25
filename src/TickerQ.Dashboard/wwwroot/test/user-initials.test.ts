import { test } from "node:test";
import assert from "node:assert/strict";

import { userInitials } from "../src/lib/user-initials.ts";

test("names and e-mail addresses give first and last initials", () => {
  assert.equal(userInitials("Jane Doe"), "JD");
  assert.equal(userInitials("jane.doe@acme.io"), "JD");
  assert.equal(userInitials("jane_mary_doe"), "JD");
});

test("a single word gives its first two letters", () => {
  assert.equal(userInitials("admin"), "AD");
  assert.equal(userInitials("x"), "X");
});

test("missing names fall back to a placeholder", () => {
  assert.equal(userInitials(null), "?");
  assert.equal(userInitials("   "), "?");
  assert.equal(userInitials("@acme.io"), "?");
});
