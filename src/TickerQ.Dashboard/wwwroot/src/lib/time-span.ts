/** Formats the invariant TimeSpan strings emitted by System.Text.Json. */
export function formatTimeSpan(value: string | null | undefined, fallback = "Unavailable", allowNegative = false): string {
  if (value == null) return fallback;
  if (value.startsWith("-")) {
    if (!allowNegative) return "Unavailable";
    const positive = formatTimeSpan(value.slice(1));
    return positive === "Unavailable" || positive === "0 seconds" ? positive : `−${positive}`;
  }
  const match = /^(?:(\d+)\.)?(\d{1,2}):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?$/.exec(value);
  if (!match || Number(match[2]) > 23) return "Unavailable";
  const days = Number(match[1] ?? 0);
  const hours = Number(match[2]);
  const minutes = Number(match[3]);
  const seconds = Number(`${match[4]}.${match[5] ?? 0}`);
  const parts: string[] = [];
  for (const [amount, unit] of [[days, "day"], [hours, "hour"], [minutes, "minute"]] as const) {
    if (amount) parts.push(`${amount} ${unit}${amount === 1 ? "" : "s"}`);
  }
  if (seconds) {
    if (!parts.length && seconds < 1)
      return `${Number((seconds * 1000).toPrecision(7))} ms`;
    parts.push(`${seconds} second${seconds === 1 ? "" : "s"}`);
  }
  return parts.join(", ") || "0 seconds";
}

/** The scheduling loop clamps a negative MinPollingInterval to zero. */
export function formatMinimumSchedulerWait(value: string | null | undefined): string {
  const formatted = formatTimeSpan(value, "Unavailable", true);
  return formatted.startsWith("−") ? "0 seconds" : formatted;
}
