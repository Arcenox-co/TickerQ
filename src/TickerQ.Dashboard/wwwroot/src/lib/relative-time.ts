/**
 * "12s ago", "5 min ago", "in 3 h": how long ago, or until, a moment is, in the largest whole unit. Moments within
 * a few seconds read "just now"; an unparseable value reads "—".
 */
export function timeAgo(value: string | null | undefined, now: number): string {
  const at = parseUtc(value);
  if (at == null) return "—";
  const seconds = Math.round((at - now) / 1000);
  const abs = Math.abs(seconds);
  if (abs < 5) return "just now";
  const span = abs < 60 ? `${abs}s`
    : abs < 3600 ? `${Math.floor(abs / 60)} min`
      : abs < 86_400 ? `${Math.floor(abs / 3600)} h`
        : abs < 30 * 86_400 ? `${Math.floor(abs / 86_400)} d`
          : `${Math.floor(abs / (30 * 86_400))} mo`;
  return seconds < 0 ? `${span} ago` : `in ${span}`;
}

/** Milliseconds since the epoch; a server time without an offset is UTC. Null when missing or unparseable. */
export function parseUtc(value: string | null | undefined): number | null {
  if (!value) return null;
  const at = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);
  return Number.isNaN(at) ? null : at;
}

/** Whether checks are late: the next one was due more than one interval ago, so no node may be checking. */
export function checksLate(nextCheckAtUtc: string | null, intervalSeconds: number, now: number): boolean {
  const next = parseUtc(nextCheckAtUtc);
  return next != null && intervalSeconds > 0 && now - next > Math.max(intervalSeconds * 1000, 30_000);
}

/** When the next check runs: "in 12s", "due now" while a node picks it up, "late" when none seems to, or "paused". */
export function nextCheckLabel(nextCheckAtUtc: string | null, intervalSeconds: number, now: number): string {
  if (!nextCheckAtUtc) return "paused";
  if (checksLate(nextCheckAtUtc, intervalSeconds, now)) return "late";
  const next = parseUtc(nextCheckAtUtc);
  return next == null || next - now < 5000 ? "due now" : timeAgo(nextCheckAtUtc, now);
}

/** Milliseconds until a list of promises is worth reading again: 2s after the soonest next check, from 5s to 30s. */
export function refreshAfterNextCheck(promises: { nextCheckAtUtc: string | null }[], now: number): number {
  const next = promises.map(p => parseUtc(p.nextCheckAtUtc)).filter((at): at is number => at != null);
  if (next.length === 0) return 30_000;
  return Math.min(30_000, Math.max(5_000, Math.min(...next) - now + 2_000));
}

/** "every 15s", "every 1 min", "every 2 h". */
export function everyLabel(seconds: number): string {
  if (seconds < 60) return `every ${seconds}s`;
  if (seconds < 3600) return `every ${Math.round(seconds / 60)} min`;
  return `every ${Math.round(seconds / 3600)} h`;
}
