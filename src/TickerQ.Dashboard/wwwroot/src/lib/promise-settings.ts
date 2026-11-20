import type { PromiseAutoFix, PromiseSettings, PromiseSettingsChange } from "../services/promise-types";

const UNITS: [seconds: number, one: string, many: string][] = [
  [86400, "1 day", "days"],
  [3600, "1 hour", "hours"],
  [60, "1 minute", "minutes"],
];

/** 60 → "1 minute", 7200 → "2 hours", 90 → "90 seconds". */
export function durationLabel(seconds: number): string {
  for (const [size, one, many] of UNITS)
    if (seconds >= size && seconds % size === 0) return seconds === size ? one : `${seconds / size} ${many}`;
  return seconds === 1 ? "1 second" : `${seconds} seconds`;
}

/** The time limits offered, besides the code's own. */
export const WITHIN_PRESETS = [60, 300, 900, 3600, 86400];

export interface WithinChoice {
  seconds: number;
  label: string;
  fromCode: boolean;
  selected: boolean;
}

/** The time limit that applies: the dashboard's, or else the code's. */
export function effectiveWithin(settings: PromiseSettings): number {
  return settings.withinSeconds ?? settings.withinSecondsFromCode;
}

/** The presets plus the code's value and the current one, shortest first, the code's marked and the current selected. */
export function withinChoices(settings: PromiseSettings): WithinChoice[] {
  const current = effectiveWithin(settings);
  const all = [...new Set([...WITHIN_PRESETS, settings.withinSecondsFromCode, current])].filter(s => s > 0).sort((a, b) => a - b);
  return all.map(seconds => ({
    seconds,
    label: durationLabel(seconds),
    fromCode: seconds === settings.withinSecondsFromCode,
    selected: seconds === current,
  }));
}

/** Picking the code's value goes back to it, so a later code change applies again. */
export function withinChange(settings: PromiseSettings, seconds: number): PromiseSettingsChange {
  return { withinSeconds: seconds === settings.withinSecondsFromCode ? null : seconds };
}

/**
 * Whether automatic fixing follows the code. The snapshot doesn't say whether the dashboard overrode it, so a setting
 * that matches the code counts as the code's.
 */
export function autoFixFollowsCode(autoFix: PromiseAutoFix): boolean {
  return autoFix.enabled === autoFix.fromCode;
}
