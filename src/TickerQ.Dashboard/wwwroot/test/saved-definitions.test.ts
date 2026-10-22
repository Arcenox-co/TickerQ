import assert from "node:assert/strict";
import test from "node:test";
import {
  filterAndSortSavedDefinitions,
  findDuplicateTemplateName,
  normalizeSavedDefinitionName,
} from "../src/lib/cron/saved-definitions.ts";
import type { SavedTickerDefinition } from "../src/services/api-types.ts";

function definition(id: string, name: string, createdAtUtc: string, description = "") {
  return {
    id,
    kind: "Template",
    target: "TimeTicker",
    name,
    description,
    createdAtUtc,
    payload: { root: { function: `Jobs.${id}`, children: [] } },
  } as unknown as SavedTickerDefinition;
}

test("template names compare case-insensitively with normalized whitespace", () => {
  const rows = [definition("one", "Daily  Close", "2026-10-20T10:00:00Z")];

  assert.equal(normalizeSavedDefinitionName(" daily\tclose "), "DAILY CLOSE");
  assert.equal(findDuplicateTemplateName(rows, " daily close ")?.id, "one");
  assert.equal(findDuplicateTemplateName(rows, "daily close", "one"), undefined);
});

test("saved work searches useful fields and orders by creation date", () => {
  const rows = [
    definition("old", "Invoice import", "2026-10-19T10:00:00Z", "Loads invoices"),
    definition("new", "Customer sync", "2026-10-21T10:00:00Z", "Updates accounts"),
  ];

  assert.deepEqual(filterAndSortSavedDefinitions(rows, "", "newest").map(x => x.id), ["new", "old"]);
  assert.deepEqual(filterAndSortSavedDefinitions(rows, "", "oldest").map(x => x.id), ["old", "new"]);
  assert.deepEqual(filterAndSortSavedDefinitions(rows, "invoice", "newest").map(x => x.id), ["old"]);
  assert.deepEqual(filterAndSortSavedDefinitions(rows, "jobs.new", "newest").map(x => x.id), ["new"]);
});
