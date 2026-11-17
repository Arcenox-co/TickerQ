import test from "node:test";
import assert from "node:assert/strict";

import {
  countOf, donePercent, donePercentText, filterPromises, findingsFor, fixable, fixLabel, healthParts, kindLabels,
  groupFindings, matchesFilter, missedByKind, noFixReason, nouns, paginate, promiseStatus, statusLabels, totalCounts,
} from "../src/lib/promise-list.ts";
import type { PromiseCounts, PromiseFinding, PromiseSnapshot } from "../src/services/promise-types.ts";

const counts = (patch: Partial<PromiseCounts> = {}): PromiseCounts => ({
  watched: 3, done: 3, running: 0, failed: 0, needsReview: 0, duplicates: 0, dismissed: 0, unmatchable: 0, ...patch,
});

function finding(patch: Partial<PromiseFinding> = {}): PromiseFinding {
  return {
    id: crypto.randomUUID(), kind: "NoJob", key: "1042", label: "Order 1042", reason: "No job was ever scheduled for it.",
    lastJob: null, fix: "NewJob", detectedAtUtc: "2026-11-15T10:00:00Z", dismissed: null, ...patch,
  };
}

function snapshot(name: string, patch: Partial<PromiseSnapshot> = {}): PromiseSnapshot {
  return {
    id: name, name, function: "ProcessOrderJob", rowType: "Order", summary: "Paid orders → ProcessOrderJob within 5 minutes",
    source: "Code",
    autoFix: { enabled: false, fromCode: false, maxPerCheck: 50, paused: false, pausedReason: null },
    settings: { paused: false, withinSeconds: null, withinSecondsFromCode: 300 },
    checkedAtUtc: "2026-11-15T10:00:00Z", durationMs: 12, error: null, truncated: false, windowStartUtc: null,
    counts: counts(), findings: [], activity: [], canManage: true,
    ...patch,
  };
}

const dismissed = { by: "ana", at: "2026-11-15T10:05:00Z", reason: null };

test("status: paused outranks a failed check, which outranks findings; an unchecked promise is neither done nor breaking", () => {
  assert.equal(promiseStatus(snapshot("a", { settings: { paused: true, withinSeconds: null, withinSecondsFromCode: 60 }, error: "boom" })), "Paused");
  assert.equal(promiseStatus(snapshot("b", { error: "boom", counts: counts({ needsReview: 2 }) })), "CheckFailed");
  assert.equal(promiseStatus(snapshot("c", { checkedAtUtc: null })), "NotChecked");
  assert.equal(promiseStatus(snapshot("d", { counts: counts({ needsReview: 1 }) })), "NeedsReview");
  assert.equal(promiseStatus(snapshot("e")), "Done");
  assert.deepEqual(Object.values(statusLabels), ["Check failed", "Needs review", "Not checked yet", "Disabled", "Done"]);
});

test("filterPromises searches name, job, row type and summary, and keeps creation order, newest first", () => {
  const promises = [
    snapshot("done", { createdAtUtc: "2026-11-01T10:00:00Z" }),
    snapshot("two", { createdAtUtc: "2026-11-03T10:00:00Z", counts: counts({ needsReview: 2 }) }),
    snapshot("one", { createdAtUtc: "2026-11-02T10:00:00Z", counts: counts({ needsReview: 1 }) }),
    snapshot("failed", { createdAtUtc: "2026-10-01T10:00:00Z", error: "boom" }),
    snapshot("invoices", { createdAtUtc: null, function: "SendInvoice", rowType: "Invoice", summary: "Invoices are sent" }),
  ];
  // What a check finds never moves a row: only when the promise was created does.
  assert.deepEqual(filterPromises(promises, "", "all").map(p => p.name), ["invoices", "two", "one", "done", "failed"]);
  assert.deepEqual(filterPromises(promises, "sendinv", "all").map(p => p.name), ["invoices"]);
  assert.deepEqual(filterPromises(promises, "INVOICE", "all").map(p => p.name), ["invoices"]);
  assert.deepEqual(filterPromises(promises, "", "NeedsReview").map(p => p.name), ["two", "one"]);
  assert.deepEqual(filterPromises(promises, "nothing", "all"), []);
});

test("paginate clamps the page so a shrinking list never shows an empty page", () => {
  assert.deepEqual(paginate([1, 2, 3, 4, 5], 9, 2), { items: [5], page: 3, pages: 3, from: 5, to: 5, total: 5 });
  assert.deepEqual(paginate([1, 2, 3], 0, 10), { items: [1, 2, 3], page: 1, pages: 1, from: 1, to: 3, total: 3 });
  assert.deepEqual(paginate([], 1, 10), { items: [], page: 1, pages: 1, from: 0, to: 0, total: 0 });
});

test("nouns speak the row type's language with a simple plural", () => {
  assert.deepEqual(nouns("Order"), { one: "order", many: "orders" });
  assert.deepEqual(nouns("Invoice"), { one: "invoice", many: "invoices" });
  assert.deepEqual(nouns("Company"), { one: "company", many: "companies" });
  assert.deepEqual(nouns("Day"), { one: "day", many: "days" });
  assert.deepEqual(nouns("OrderLine"), { one: "order line", many: "order lines" });
  assert.deepEqual(nouns("Address"), { one: "address", many: "addresses" });
  assert.deepEqual(nouns("Shop.Billing.Box"), { one: "box", many: "boxes" });
  assert.deepEqual(nouns(""), { one: "row", many: "rows" });
  assert.deepEqual(nouns(null), { one: "row", many: "rows" });
  assert.equal(countOf(1, nouns("Order")), "1 order");
  assert.equal(countOf(3, nouns("Order")), "3 orders");
  assert.equal(countOf(0, nouns("Order")), "0 orders");
});

test("health parts always add up to watched", () => {
  const sum = (c: PromiseCounts) => healthParts(c).reduce((s, p) => s + p.value, 0);
  const cases = [
    counts({ watched: 10, done: 5, running: 1, needsReview: 2, failed: 1, dismissed: 1 }),
    counts({ watched: 0, done: 0 }),
    counts({ watched: 5, done: 9, running: 3 }),
    counts({ watched: 4, done: 0, running: 0, needsReview: 0, failed: 0, unmatchable: 4 }),
    counts({ watched: 7, done: -1, needsReview: 3 }),
  ];
  for (const c of cases) {
    assert.equal(sum(c), c.watched);
    assert.ok(healthParts(c).every(p => p.value >= 0));
  }
  assert.deepEqual(healthParts(cases[0]).map(p => [p.key, p.value]),
    [["done", 5], ["running", 1], ["needsReview", 2], ["failed", 1], ["other", 1]]);
  // More done than watched never overflows the bar.
  assert.deepEqual(healthParts(cases[2]).map(p => p.value), [5, 0, 0, 0, 0]);
});

test("totals and done percent", () => {
  const total = totalCounts([snapshot("a", { counts: counts({ watched: 4, done: 1, needsReview: 3 }) }), snapshot("b")]);
  assert.deepEqual(total, { watched: 7, done: 4, running: 0, failed: 0, needsReview: 3, duplicates: 0, dismissed: 0, unmatchable: 0 });
  assert.equal(donePercent(total), 57);
  assert.equal(donePercent({ watched: 0, done: 0 }), null);
  assert.equal(donePercentText({ watched: 0, done: 0 }), "–");
  assert.equal(donePercentText({ watched: 2, done: 2 }), "100%");
});

test("kinds use the same words everywhere", () => {
  assert.deepEqual(kindLabels, {
    NoJob: "Never scheduled", NeverRan: "Never ran", DoneButNotApplied: "Job ran, row not updated",
    WillRunTwice: "Will run twice", RanTwice: "Ran twice",
  });
});

test("rows that ran twice need review but are not missed rows in the health bar", () => {
  const parts = healthParts(counts({ watched: 10, done: 8, needsReview: 3, duplicates: 2 }));
  assert.deepEqual(parts.map(p => [p.key, p.value]), [["done", 8], ["running", 0], ["needsReview", 1], ["failed", 0], ["other", 1]]);
});

test("missed by kind counts open findings only, in a fixed order", () => {
  const p = snapshot("a", {
    findings: [finding(), finding({ kind: "NeverRan" }), finding({ dismissed }), finding({ kind: "NoJob" }), finding({ kind: "RanTwice" })],
  });
  assert.deepEqual(missedByKind([p, snapshot("b")]), [
    { kind: "NoJob", count: 2 }, { kind: "NeverRan", count: 1 }, { kind: "DoneButNotApplied", count: 0 },
    { kind: "WillRunTwice", count: 0 }, { kind: "RanTwice", count: 1 },
  ]);
});

test("an extra waiting job is cancelled; a row that ran twice is only reported", () => {
  assert.equal(fixLabel(finding({ kind: "WillRunTwice", fix: "CancelExtra" })), "Cancel extra job");
  assert.equal(fixLabel(finding({ kind: "RanTwice", fix: "None" })), null);
  assert.equal(noFixReason(finding({ kind: "RanTwice", fix: "None" })), null);
});

test("findings split into open and dismissed and filter by kind", () => {
  const all = [finding({ id: "1" }), finding({ id: "2", kind: "NeverRan" }), finding({ id: "3", dismissed })];
  assert.deepEqual(findingsFor(all, "open", null).map(f => f.id), ["1", "2"]);
  assert.deepEqual(findingsFor(all, "open", "NeverRan").map(f => f.id), ["2"]);
  assert.deepEqual(findingsFor(all, "dismissed", null).map(f => f.id), ["3"]);
});

test("fix buttons: a new job, a waiting job, or a reason why not", () => {
  assert.equal(fixLabel(finding({ fix: "NewJob" })), "Schedule job");
  assert.equal(fixLabel(finding({ fix: "NewJob", kind: "NeverRan" })), "Schedule again");
  assert.equal(fixLabel(finding({ fix: "RunNow", kind: "NeverRan" })), "Start now");
  const reported = finding({ fix: "None", kind: "DoneButNotApplied" });
  assert.equal(fixLabel(reported), null);
  assert.equal(noFixReason(reported), null);
  assert.ok(noFixReason(finding({ fix: "None", kind: "NeverRan" })));
  assert.equal(noFixReason(finding()), null);
  const all = [finding({ id: "a" }), finding({ id: "b", fix: "None" }), finding({ id: "c", dismissed }), finding({ id: "d", fix: "RunNow" })];
  assert.deepEqual(fixable(all).map(f => f.id), ["a", "d"]);
});

test("findings group by kind in review order, buttons first", () => {
  const groups = groupFindings([
    finding({ id: "1", kind: "RanTwice" }), finding({ id: "2" }), finding({ id: "3", kind: "WillRunTwice" }), finding({ id: "4" }),
  ]);
  assert.deepEqual(groups.map(g => [g.kind, g.findings.map(f => f.id)]), [["NoJob", ["2", "4"]], ["WillRunTwice", ["3"]], ["RanTwice", ["1"]]]);
});

test("a disabled promise is under Disabled and still under what its last check found", () => {
  const disabled = snapshot("d", { settings: { paused: true, withinSeconds: null, withinSecondsFromCode: 60 }, counts: counts({ needsReview: 2 }) });
  assert.equal(matchesFilter(disabled, "Paused"), true);
  assert.equal(matchesFilter(disabled, "NeedsReview"), true);
  assert.equal(matchesFilter(snapshot("e", { counts: counts({ needsReview: 2 }) }), "Paused"), false);
  assert.deepEqual(filterPromises([disabled, snapshot("e", { counts: counts({ needsReview: 2 }) })], "", "NeedsReview").map(p => p.name), ["d", "e"]);
});

test("creation times without an offset are UTC, so old and new promises sort together", () => {
  const promises = [
    snapshot("old", { createdAtUtc: "2026-11-15T17:01:46.459693" }),
    snapshot("new", { createdAtUtc: "2026-11-15T17:30:00Z" }),
  ];
  assert.deepEqual(filterPromises(promises, "", "all").map(p => p.name), ["new", "old"]);
});
