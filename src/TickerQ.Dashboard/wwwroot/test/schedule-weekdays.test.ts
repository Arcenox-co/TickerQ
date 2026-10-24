import test from "node:test";
import assert from "node:assert/strict";
import { expandWeekdays } from "../src/lib/schedule-weekdays.ts";

test("named groups expand to their days", () => {
  assert.deepEqual(expandWeekdays("All").length, 7);
  assert.deepEqual(expandWeekdays("MondayToFriday"), ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]);
  assert.deepEqual(expandWeekdays("Weekend"), ["Saturday", "Sunday"]);
});

test("a group combined with single days keeps every day", () => {
  assert.deepEqual(expandWeekdays("MondayToFriday, Saturday"),
    ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]);
  assert.deepEqual(expandWeekdays("Sunday, MondayToFriday"),
    ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Sunday"]);
});

test("single days are ordered Monday first and unknown or empty parts are ignored", () => {
  assert.deepEqual(expandWeekdays("Sunday, Monday"), ["Monday", "Sunday"]);
  assert.deepEqual(expandWeekdays("monday,  ,Funday"), ["Monday"]);
  assert.deepEqual(expandWeekdays(""), []);
  assert.deepEqual(expandWeekdays(null), []);
});
