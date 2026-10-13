// Pure per-function request-payload draft state for the create dialogs.
//
// The request editor must initialize a fresh payload ONLY when the user
// intentionally switches to a different function. It must NOT reset when the
// function's metadata object is merely refreshed (e.g. a React Query refetch
// hands back an equivalent object under the same name) — that would clobber the
// user's in-progress raw/schema edits. It must also preserve each function's
// draft when the user flips A -> B -> A, so returning to a function restores
// what they had typed rather than re-initializing.

export interface FunctionDraftState {
  /** The function whose draft is currently loaded, or null before first load. */
  active: string | null;
  /** Saved payload drafts keyed by function name. */
  drafts: Record<string, string>;
}

/** Fresh, empty draft book — also the correct "reset on reopen/close" value. */
export function createDraftState(): FunctionDraftState {
  return { active: null, drafts: {} };
}

/**
 * Reconcile the draft book toward `target`.
 *
 * - Same name as `active`: no-op (returns the live value untouched). This is the
 *   guard that makes an equivalent-metadata refresh a non-event.
 * - Different name: persist the outgoing function's `liveValue`, then resolve
 *   the target's payload — a previously saved draft if one exists, otherwise a
 *   freshly initialized payload via `initialize`.
 *
 * The returned `value` is what the form should display; `changed` tells the
 * caller whether to write it back (and clear stale validation errors).
 */
export function selectFunction(
  state: FunctionDraftState,
  target: string,
  liveValue: string,
  initialize: (functionName: string) => string,
): { state: FunctionDraftState; value: string; changed: boolean } {
  if (state.active === target) {
    return { state, value: liveValue, changed: false };
  }

  const drafts = { ...state.drafts };
  if (state.active !== null) drafts[state.active] = liveValue;

  const value = target in drafts ? drafts[target] : initialize(target);
  return { state: { active: target, drafts }, value, changed: true };
}
