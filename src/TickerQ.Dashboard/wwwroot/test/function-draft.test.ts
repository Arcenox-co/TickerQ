import { test } from "node:test";
import assert from "node:assert/strict";

import {
  createDraftState,
  selectFunction,
} from "../src/lib/function-draft.ts";

// Deterministic initializer used by the tests: each function gets a distinct
// "fresh" payload so we can tell an initialize apart from a preserved draft.
const initialFor = (fn: string) => `INIT:${fn}`;

test("first selection initializes the target function's payload", () => {
  const state = createDraftState();
  const result = selectFunction(state, "A", "", initialFor);
  assert.equal(result.changed, true);
  assert.equal(result.value, "INIT:A");
  assert.equal(result.state.active, "A");
});

test("re-selecting the same name is a no-op (metadata refresh must not reset)", () => {
  let { state } = selectFunction(createDraftState(), "A", "", initialFor);
  // Simulate a user edit living in the form.
  const edited = '{"user":"edited A"}';
  const again = selectFunction(state, "A", edited, initialFor);
  assert.equal(again.changed, false);
  // Value is left as-is (the live edited value), NOT re-initialized.
  assert.equal(again.value, edited);
  state = again.state;
  // Still no reset on a third identical refresh.
  const third = selectFunction(state, "A", edited, initialFor);
  assert.equal(third.changed, false);
  assert.equal(third.value, edited);
});

test("preserves per-function drafts across A -> B -> A", () => {
  let state = createDraftState();

  // Load A, user edits it.
  let r = selectFunction(state, "A", "", initialFor);
  state = r.state;
  const editedA = '{"raw":"A edits"}';

  // Switch to B (carrying A's live edited value); B initializes fresh.
  r = selectFunction(state, "B", editedA, initialFor);
  state = r.state;
  assert.equal(r.changed, true);
  assert.equal(r.value, "INIT:B");
  const editedB = '{"schema":"B edits"}';

  // Switch back to A (carrying B's live value); A's draft must be restored.
  r = selectFunction(state, "A", editedB, initialFor);
  state = r.state;
  assert.equal(r.changed, true);
  assert.equal(r.value, editedA, "A draft should be preserved, not re-initialized");

  // And B's draft is still remembered.
  r = selectFunction(state, "B", editedA, initialFor);
  assert.equal(r.value, editedB, "B draft should be preserved");
});

test("createDraftState yields an isolated, empty book (reset on reopen/close)", () => {
  let { state } = selectFunction(createDraftState(), "A", "", initialFor);
  ({ state } = selectFunction(state, "A", '{"dirty":true}', initialFor));

  // A brand-new state (as used on dialog reopen) knows nothing of prior drafts.
  const fresh = createDraftState();
  const r = selectFunction(fresh, "A", "", initialFor);
  assert.equal(r.value, "INIT:A");
  assert.notEqual(fresh, state);
});
