import { format as timeago } from 'timeago.js';

type DateFormatRegion = 'eu' | 'us' | 'iso';

const europeanZonePrefixes = ['Europe/', 'Africa/'];
const americanZonePrefixes = ['America/', 'US/'];

// Fallback patterns for Windows timezone IDs that didn't get converted to IANA on the backend
const windowsEuropeanPatterns = ['W. Europe', 'Central Europe', 'E. Europe', 'GMT ', 'Greenwich'];
const windowsAmericanPatterns = ['Eastern', 'Central Standard', 'Mountain', 'Pacific', 'Alaskan', 'Hawaiian', 'US '];

export function getDateFormatRegion(timeZone?: string): DateFormatRegion {
    if (!timeZone) return 'iso';

    if (europeanZonePrefixes.some(prefix => timeZone.startsWith(prefix))) {
        return 'eu';
    }

    if (americanZonePrefixes.some(prefix => timeZone.startsWith(prefix))) {
        return 'us';
    }

    // Handle Windows timezone IDs as fallback (check European first to avoid
    // "Central European" matching the American "Central Standard" pattern)
    if (windowsEuropeanPatterns.some(p => timeZone.startsWith(p))) {
        return 'eu';
    }

    if (windowsAmericanPatterns.some(p => timeZone.startsWith(p))) {
        return 'us';
    }

    return 'iso';
}

export function buildDatePart(region: DateFormatRegion, year: string, month: string, day: string): string {
    switch (region) {
        case 'eu': return `${day}/${month}/${year}`;
        case 'us': return `${month}/${day}/${year}`;
        default:   return `${year}-${month}-${day}`;
    }
}

export function formatDate(
    utcDateString: string,
    includeTime = true,
    timeZone?: string
): string {
    if (!utcDateString) {
        return '';
    }

    // Ensure there is a "Z" so JS treats it as UTC
    let iso = utcDateString.trim();
    if (!iso.endsWith('Z')) {
        iso = iso.replace(' ', 'T') + 'Z';
    }

    const dateObj = new Date(iso);

    const baseOptions: Intl.DateTimeFormatOptions = {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
    };

    if (includeTime) {
        baseOptions.hour = '2-digit';
        baseOptions.minute = '2-digit';
        baseOptions.second = '2-digit';
        baseOptions.hour12 = false;
    }

    // Try with the provided timezone; fall back to no timezone if it's invalid (e.g. Windows timezone ID)
    let formatter: Intl.DateTimeFormat;
    let effectiveTimeZone = timeZone;
    try {
        formatter = new Intl.DateTimeFormat('en-CA', { ...baseOptions, ...(timeZone ? { timeZone } : {}) });
    } catch {
        effectiveTimeZone = undefined;
        formatter = new Intl.DateTimeFormat('en-CA', baseOptions);
    }

    const parts = formatter.formatToParts(dateObj);
    const get = (type: string) => parts.find(p => p.type === type)?.value ?? '';

    const region = getDateFormatRegion(effectiveTimeZone);
    const datePart = buildDatePart(region, get('year'), get('month'), get('day'));

    if (!includeTime) {
        return datePart;
    }
    return `${datePart} ${get('hour')}:${get('minute')}:${get('second')}`;
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

export function formatTimeAgo(date: string | Date): string {
    // Front-end often passes dates as strings straight up from JSON payloads.
    // All dates on back-end are UTC but dates loaded by EF have DateTimeKind.Unspecified by default,
    // which is serialized to JSON without any offset suffix.
    // We have to specify them as UTC so that they are not parsed as local time by JS.
    if (typeof date === 'string' && !date.endsWith('Z')) {
        date = date + 'Z'
    }
    return timeago(date)
}
