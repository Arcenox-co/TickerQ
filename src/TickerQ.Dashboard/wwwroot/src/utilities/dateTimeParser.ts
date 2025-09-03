export function formatDate(
  utcDateString: string,
  includeTime = true
): string {
  if (!utcDateString) {
    // nothing to format, return empty (or some placeholder)
    return '';
  }
  // 1) Ensure there’s a “Z”
  let iso = utcDateString.trim();

  //commented out to let it works with LocalDatetime
  //if (!iso.endsWith('Z')) {
  //    iso = iso.replace(' ', 'T') + 'Z';
  //}

  // 2) Now JS knows “that’s UTC” and will shift to local when you read getHours()
  const dateObj = new Date(iso);

  // 3) Extract with local getters
  const dd = String(dateObj.getDate()).padStart(2, '0');
  const MM = String(dateObj.getMonth() + 1).padStart(2, '0');
  const yyyy = dateObj.getFullYear();

  if (!includeTime) {
    return `${dd}.${MM}.${yyyy}`;
  }

  const hh = String(dateObj.getHours()).padStart(2, '0');
  const mm = String(dateObj.getMinutes()).padStart(2, '0');
  const ss = String(dateObj.getSeconds()).padStart(2, '0');

  return `${dd}.${MM}.${yyyy} ${hh}:${mm}:${ss}`;
}

export function formatTime(time: number, inputInMilliseconds = false): string {
  if (inputInMilliseconds && time < 1000) {
    return time + 'ms';
  }

  const seconds = inputInMilliseconds ? Math.floor(time / 1000) : time;

  if (seconds < 60) {
    return seconds + 's';
  }

  if (seconds < 3600) {
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;
    return remainingSeconds === 0
      ? minutes + 'm'
      : minutes + 'm ' + remainingSeconds + 's';
  }

  if (seconds < 86400) {
    const hours = Math.floor(seconds / 3600);
    const remainingMinutes = Math.floor((seconds % 3600) / 60);
    return remainingMinutes === 0
      ? hours + 'h'
      : hours + 'h ' + remainingMinutes + 'm';
  }

  const days = Math.floor(seconds / 86400);
  const remainingHours = Math.floor((seconds % 86400) / 3600);
  return remainingHours === 0
    ? days + 'd'
    : days + 'd ' + remainingHours + 'h';
}

export function formatDateToUtc(date: string): string {
  // Parse string manually as local time
  const [year, month, day, hour, minute] = date.split(/[-T:]/).map(Number)

  // Create Date using local time (month is 0-based)
  const localDate = new Date(year, month - 1, day, hour, minute)

  // Convert to UTC ISO string
  return localDate.toISOString()
}

export function formatFromUtcToLocal(utcDateString: string): string {
  const utcDate = new Date(utcDateString) // Interprets as UTC
  const year = utcDate.getFullYear()
  const month = String(utcDate.getMonth() + 1).padStart(2, '0')
  const day = String(utcDate.getDate()).padStart(2, '0')
  const hours = String(utcDate.getHours()).padStart(2, '0')
  const minutes = String(utcDate.getMinutes()).padStart(2, '0')
  const seconds = String(utcDate.getSeconds()).padStart(2, '0')

  return `${year}-${month}-${day}T${hours}:${minutes}:${seconds}`
}
