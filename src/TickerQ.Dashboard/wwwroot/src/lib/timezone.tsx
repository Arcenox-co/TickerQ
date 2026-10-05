import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useDashboardOptions } from "@/services/hooks";
import { getRuntimeConfig } from "@/lib/runtime-config";

const STORAGE_KEY = "tickerq-dashboard-timezone";

// ──────────── display aliases ────────────
//
// IANA has no `Europe/Pristina` — Kosovo is assigned to `Europe/Belgrade` in
// the tz database, so browsers in Kosovo report "Belgrade". This alias layer
// lets those users see and pick "Pristina" while ALL date math stays on the
// canonical id (passing a non-existent zone to Intl throws). Do not "clean
// this up": functional code must canonicalize via canonicalizeTz(); only
// presentation uses the alias id.

export interface TzDisplayAlias {
  /** Real IANA zone used for every Intl call. */
  canonical: string;
  /** Presentation-only id shown/persisted when the user prefers this alias. */
  displayId: string;
  /**
   * Lowercase browser-locale hints that prefer this alias. A leading "-"
   * matches a region suffix ("-xk" → "sr-XK", "en-XK"); otherwise matches the
   * language ("sq" → "sq", "sq-AL").
   */
  localeHints: string[];
}

export const TZ_DISPLAY_ALIASES: TzDisplayAlias[] = [
  {
    canonical: "Europe/Belgrade",
    displayId: "Europe/Pristina",
    localeHints: ["sq", "-xk"],
  },
];

/** Resolve a possibly-aliased id to the real IANA zone. Safe for any Intl call. */
export function canonicalizeTz(id: string): string {
  const alias = TZ_DISPLAY_ALIASES.find((a) => a.displayId === id);
  return alias ? alias.canonical : id;
}

/**
 * The id to *show* for a canonical zone, based on the browser locale.
 * A Serbian browser (sr-RS) keeps "Europe/Belgrade"; a Kosovar browser
 * (sq, sq-AL, sr-XK, en-XK) gets "Europe/Pristina". Ambiguous locales fall
 * back to the canonical id — the picker still offers both entries.
 */
export function preferredDisplayId(canonical: string): string {
  const langs =
    typeof navigator !== "undefined"
      ? (navigator.languages?.length ? navigator.languages : [navigator.language]).map((l) =>
          (l ?? "").toLowerCase()
        )
      : [];

  const alias = TZ_DISPLAY_ALIASES.find(
    (a) =>
      a.canonical === canonical &&
      a.localeHints.some((hint) =>
        hint.startsWith("-")
          ? langs.some((l) => l.endsWith(hint))
          : langs.some((l) => l === hint || l.startsWith(hint + "-"))
      )
  );
  return alias ? alias.displayId : canonical;
}

interface TimezoneContextValue {
  /** The resolved timezone for date math (always a REAL IANA name — aliases canonicalized). */
  timezone: string;
  /**
   * The timezone id to *display* — may be a presentation alias like
   * "Europe/Pristina". Same instant math as `timezone`; labels only.
   */
  displayTimezone: string;
  /** Persist a user override (alias ids allowed); pass `null` to clear and revert to defaults. */
  setTimezone: (tz: string | null) => void;
  /** Dashboard-configured default (SetTimeZone on the backend), or null when not set. */
  configTimezone: string | null;
  /** The scheduler's configured timezone, or null until /api/options resolves. */
  schedulerTimezone: string | null;
  /** The browser's local timezone, useful as a "reset to local" affordance. */
  browserTimezone: string;
  /** True when the user has set an explicit override (persisted in localStorage). */
  isUserOverride: boolean;
}

const TimezoneContext = createContext<TimezoneContextValue | null>(null);

function readStored(): string | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeStored(tz: string | null) {
  try {
    if (typeof window === "undefined") return;
    if (tz) window.localStorage.setItem(STORAGE_KEY, tz);
    else window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* localStorage unavailable — fall back to in-memory only */
  }
}

/**
 * Resolution order for the timezone in use:
 *   1. User override (persisted in localStorage), if set
 *   2. Dashboard-configured timezone (SetTimeZone on the backend, via runtime config)
 *   3. Scheduler timezone from /api/options
 *   4. Browser local timezone (fallback during first load / if API fails)
 *
 * Setting `null` via `setTimezone` clears the override and falls back to (2)/(3)/(4).
 */
export function TimezoneProvider({ children }: { children: ReactNode }) {
  const [override, setOverride] = useState<string | null>(() => readStored());
  const { data: options } = useDashboardOptions();

  const browserTimezone = useMemo(
    () => Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
    []
  );
  const configTimezone = getRuntimeConfig().timezone;
  const schedulerTimezone = options?.schedulerTimeZone ?? null;

  // If the user has no override but the browser's localStorage is updated by
  // another tab, sync. Cheap to wire and matches typical multi-tab apps.
  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === STORAGE_KEY) setOverride(e.newValue);
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  const setTimezone = useCallback((tz: string | null) => {
    setOverride(tz);
    writeStored(tz);
  }, []);

  const value = useMemo<TimezoneContextValue>(() => {
    // User overrides are stored verbatim (may be an alias id like
    // Europe/Pristina). Defaults get the locale-preferred display form so a
    // Kosovar browser shows Pristina while a Serbian one shows Belgrade —
    // both resolve to the same canonical zone for date math.
    const displayTimezone =
      override ??
      preferredDisplayId(configTimezone ?? schedulerTimezone ?? browserTimezone);
    return {
      timezone: canonicalizeTz(displayTimezone),
      displayTimezone,
      setTimezone,
      configTimezone,
      schedulerTimezone,
      browserTimezone,
      isUserOverride: !!override,
    };
  }, [override, configTimezone, schedulerTimezone, browserTimezone, setTimezone]);

  return <TimezoneContext.Provider value={value}>{children}</TimezoneContext.Provider>;
}

export function useTimezone(): TimezoneContextValue {
  const ctx = useContext(TimezoneContext);
  if (!ctx) {
    throw new Error("useTimezone must be used inside <TimezoneProvider>.");
  }
  return ctx;
}

/**
 * Curated list of distinct timezones for the picker — one canonical city per
 * standard-time UTC offset (plus the half/quarter-hour exceptions). Anyone
 * needing a different zone still gets covered by the "Browser local" and
 * "Scheduler default" quick options at the top of the picker.
 */
export function listIanaTimezones(): string[] {
  return [
    "UTC",
    "Pacific/Honolulu",       // -10:00 (no DST)
    "America/Anchorage",      // -09:00 / -08:00
    "America/Los_Angeles",    // -08:00 / -07:00  (PST/PDT)
    "America/Denver",         // -07:00 / -06:00  (MST/MDT)
    "America/Chicago",        // -06:00 / -05:00  (CST/CDT)
    "America/New_York",       // -05:00 / -04:00  (EST/EDT)
    "America/Halifax",        // -04:00 / -03:00  (AST/ADT)
    "America/Sao_Paulo",      // -03:00
    "Atlantic/Azores",        // -01:00 / +00:00
    "Europe/London",          // +00:00 / +01:00  (GMT/BST)
    "Europe/Berlin",          // +01:00 / +02:00  (CET/CEST — incl. Paris, Madrid, Rome, Amsterdam)
    "Europe/Belgrade",        // +01:00 / +02:00  (CET/CEST — has a "Pristina" display alias, see TZ_DISPLAY_ALIASES)
    "Europe/Tirane",          // +01:00 / +02:00  (CET/CEST — incl. Ljubljana, Zagreb)
    "Europe/Athens",          // +02:00 / +03:00  (EET/EEST — incl. Helsinki, Kyiv)
    "Europe/Moscow",          // +03:00 (no DST)
    "Asia/Dubai",             // +04:00
    "Asia/Karachi",           // +05:00
    "Asia/Kolkata",           // +05:30  (distinctive half-hour offset)
    "Asia/Kathmandu",         // +05:45  (distinctive quarter-hour offset)
    "Asia/Dhaka",             // +06:00
    "Asia/Yangon",            // +06:30
    "Asia/Bangkok",           // +07:00
    "Asia/Shanghai",          // +08:00  (incl. HK, Singapore, Manila)
    "Asia/Tokyo",             // +09:00  (incl. Seoul)
    "Australia/Adelaide",     // +09:30 / +10:30
    "Australia/Sydney",       // +10:00 / +11:00
    "Pacific/Auckland",       // +12:00 / +13:00
  ];
}
