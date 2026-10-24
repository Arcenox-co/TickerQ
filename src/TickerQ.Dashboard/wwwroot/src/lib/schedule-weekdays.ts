export const weekdays = [
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
  "Sunday",
] as const;

export type Weekday = typeof weekdays[number];

const groups: Record<string, readonly Weekday[]> = {
  all: weekdays,
  mondaytofriday: weekdays.slice(0, 5),
  weekend: weekdays.slice(5),
};

/**
 * Expands a ScheduleWeekdays flags value into individual days, Monday first. The server
 * serializes flags with their named groups, so a value such as "MondayToFriday, Saturday"
 * must expand every comma-separated part, not only a lone group name.
 */
export function expandWeekdays(value: string | null | undefined): Weekday[] {
  const selected = new Set<Weekday>();
  for (const part of (value ?? "").split(",")) {
    const key = part.trim().toLowerCase();
    if (!key) continue;
    const group = groups[key];
    if (group) {
      group.forEach((day) => selected.add(day));
      continue;
    }
    const day = weekdays.find((item) => item.toLowerCase() === key);
    if (day) selected.add(day);
  }
  return weekdays.filter((day) => selected.has(day));
}
