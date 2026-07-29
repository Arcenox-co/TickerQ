import { formatDistanceToNow } from "date-fns";

/**
 * Parse a backend-issued date string as UTC.
 *
 * TickerQ stores everything in UTC (DateTime.UtcNow throughout) but the JSON
 * serializer writes those values WITHOUT a "Z" suffix (e.g.
 * `"2026-06-05T13:30:00"`). Plain `new Date(s)` interprets that as LOCAL
 * time, which shifts everything by the viewer's UTC offset — that's why a
 * just-added ticker can show "2 hours ago" on a `UTC+02:00` machine.
 *
 * Strings that already carry a `Z`/offset, plus numbers and Dates, pass
 * through untouched.
 */
export function parseUtc(value: string | number | Date): Date {
  if (value instanceof Date) return value;
  if (typeof value === "number") return new Date(value);
  const trimmed = value.trim();
  return /[zZ]|[+-]\d{2}:?\d{2}$/.test(trimmed)
    ? new Date(trimmed)
    : new Date(trimmed + "Z");
}

export function relativeTime(dateStr: string | null | undefined): string {
  if (!dateStr) return "—";
  try {
    return formatDistanceToNow(parseUtc(dateStr), { addSuffix: true });
  } catch {
    return dateStr;
  }
}

export function formatDuration(ms?: number | null): string {
  if (ms == null) return "—";
  if (ms < 1) return "<1ms";
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  return `${(ms / 60000).toFixed(1)}m`;
}

export function truncate(str: string, len: number = 40): string {
  return str.length > len ? str.slice(0, len) + "…" : str;
}

/**
 * Compact tabular form for time columns: `YYYY-MM-DD HH:mm` in the given
 * timezone. Same locale-stable Gregorian as {@link formatAbsolute} just
 * without seconds — fits comfortably in table cells while still shifting
 * visibly when the user changes timezone.
 */
export function formatShort(
  value: string | number | Date | null | undefined,
  tz: string
): string {
  if (value == null) return "—";
  const d = parseUtc(value);
  if (Number.isNaN(d.getTime())) return String(value);
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: tz,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).formatToParts(d);
  const pick = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? "";
  return `${pick("year")}-${pick("month")}-${pick("day")} ${pick("hour")}:${pick("minute")}`;
}

/**
 * Tooltip-friendly long form: combines a full absolute timestamp (with seconds)
 * and the relative "X ago" so hovering a time cell always tells the full story.
 */
export function formatTooltip(
  value: string | number | Date | null | undefined,
  tz: string
): string {
  if (value == null) return "";
  const abs = formatAbsolute(value, tz);
  const rel = typeof value === "string" ? relativeTime(value) : "";
  return rel ? `${abs} · ${rel}` : abs;
}

export function formatLogTimestamp(unixMs: number, tz?: string): string {
  if (!tz) {
    // Legacy fallback when no timezone is in scope (e.g. unit tests). UTC.
    return new Date(unixMs).toISOString().replace("T", " ").slice(0, 23);
  }
  return formatAbsolute(new Date(unixMs), tz, { withMillis: true });
}

/**
 * Format an absolute timestamp in the given IANA timezone as
 * "YYYY-MM-DD HH:mm:ss" (or with millis when `withMillis` is true).
 * Locale-stable: always uses 24-hour Gregorian — only the timezone shifts.
 *
 * Use this whenever a user-visible UI element shows an actual point in time
 * (detail rails, flowchart node tooltips, cron next-run hint, log timestamps).
 * Use {@link relativeTime} for "X minutes ago" style — those are delta-based
 * and need no timezone.
 */
export function formatAbsolute(
  value: string | number | Date | null | undefined,
  tz: string,
  opts: { withMillis?: boolean } = {}
): string {
  if (value == null) return "—";
  const d = parseUtc(value);
  if (Number.isNaN(d.getTime())) return String(value);
  const fmt = new Intl.DateTimeFormat("en-CA", {
    timeZone: tz,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
    ...(opts.withMillis ? ({ fractionalSecondDigits: 3 } as Intl.DateTimeFormatOptions) : {}),
  });
  const parts = fmt.formatToParts(d);
  const pick = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? "";
  const date = `${pick("year")}-${pick("month")}-${pick("day")}`;
  const time = `${pick("hour")}:${pick("minute")}:${pick("second")}`;
  const ms = opts.withMillis ? `.${pick("fractionalSecond" as Intl.DateTimeFormatPartTypes)}` : "";
  return `${date} ${time}${ms}`;
}
