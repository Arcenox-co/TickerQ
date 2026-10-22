import assert from "node:assert/strict";
import test from "node:test";
import {
  bucketsToVolume,
  mergeVolumeData,
} from "../src/lib/cron/execution-volume.ts";
import type { GraphBucketDto } from "../src/services/api-types.ts";

function bucket(date: string, counts: GraphBucketDto["counts"]): GraphBucketDto {
  return { date, counts };
}

test("volume conversion preserves every persisted ticker status", () => {
  const [point] = bucketsToVolume([
    bucket("2026-10-22T00:00:00Z", [
      { status: "Idle", count: 1 },
      { status: "Queued", count: 2 },
      { status: "InProgress", count: 3 },
      { status: "Done", count: 4 },
      { status: "DueDone", count: 5 },
      { status: "Failed", count: 6 },
      { status: "Cancelled", count: 7 },
      { status: "Skipped", count: 8 },
    ]),
  ]);

  assert.deepEqual(
    {
      Idle: point.Idle,
      Queued: point.Queued,
      InProgress: point.InProgress,
      Done: point.Done,
      DueDone: point.DueDone,
      Failed: point.Failed,
      Cancelled: point.Cancelled,
      Skipped: point.Skipped,
    },
    {
      Idle: 1,
      Queued: 2,
      InProgress: 3,
      Done: 4,
      DueDone: 5,
      Failed: 6,
      Cancelled: 7,
      Skipped: 8,
    }
  );
});

test("aggregate volume adds time and cron executions without dropping statuses", () => {
  const time = bucketsToVolume([
    bucket("2026-10-22T00:00:00Z", [
      { status: "Idle", count: 2 },
      { status: "Done", count: 3 },
    ]),
  ]);
  const cron = bucketsToVolume([
    bucket("2026-10-22T00:00:00Z", [
      { status: "Idle", count: 5 },
      { status: "Failed", count: 7 },
    ]),
  ]);

  const [combined] = mergeVolumeData(time, cron);
  assert.equal(combined.Idle, 7);
  assert.equal(combined.Done, 3);
  assert.equal(combined.Failed, 7);
  assert.equal(combined.Queued, 0);
});
