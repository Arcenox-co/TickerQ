import type { GraphBucketDto } from "../../services/api-types.ts";
import { parseUtc } from "./format.ts";

export interface VolumePoint {
  hour: string;
  Done: number;
  Failed: number;
  DueDone: number;
  Cancelled: number;
  Skipped: number;
  Queued: number;
  InProgress: number;
  Idle: number;
}

export type VolumeStatusKey = Exclude<keyof VolumePoint, "hour">;

export const VOLUME_STATUS_KEYS: readonly VolumeStatusKey[] = [
  "Idle",
  "Queued",
  "InProgress",
  "Done",
  "DueDone",
  "Failed",
  "Cancelled",
  "Skipped",
];

function emptyPoint(hour: string): VolumePoint {
  return {
    hour,
    Done: 0,
    Failed: 0,
    DueDone: 0,
    Cancelled: 0,
    Skipped: 0,
    Queued: 0,
    InProgress: 0,
    Idle: 0,
  };
}

export function bucketsToVolume(buckets: GraphBucketDto[] | undefined): VolumePoint[] {
  if (!buckets) return [];
  return buckets.map((bucket) => {
    const point = emptyPoint(
      parseUtc(bucket.date).toLocaleDateString(undefined, {
        month: "short",
        day: "numeric",
      })
    );
    for (const count of bucket.counts) point[count.status] += count.count;
    return point;
  });
}

/** Combines aligned time-ticker and cron-occurrence series for aggregate pages. */
export function mergeVolumeData(...dataSets: readonly VolumePoint[][]): VolumePoint[] {
  const merged = new Map<string, VolumePoint>();
  for (const data of dataSets) {
    for (const point of data) {
      const target = merged.get(point.hour) ?? emptyPoint(point.hour);
      for (const status of VOLUME_STATUS_KEYS) target[status] += point[status];
      if (!merged.has(point.hour)) merged.set(point.hour, target);
    }
  }
  return [...merged.values()];
}

export function emptyVolumeData(days = 7): VolumePoint[] {
  const out: VolumePoint[] = [];
  const now = new Date();
  for (let i = days - 1; i >= 0; i--) {
    const date = new Date(now);
    date.setDate(now.getDate() - i);
    out.push(
      emptyPoint(date.toLocaleDateString(undefined, { month: "short", day: "numeric" }))
    );
  }
  return out;
}
