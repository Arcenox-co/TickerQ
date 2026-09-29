import { useEffect, useMemo, useState } from "react";
import { Check, Clock, Globe, MapPin, RotateCcw, Server } from "lucide-react";

import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@/components/ui/command";
import {
  TZ_DISPLAY_ALIASES,
  canonicalizeTz,
  listIanaTimezones,
  preferredDisplayId,
  useTimezone,
} from "@/lib/timezone";
import { cn } from "@/lib/utils";

/**
 * One selectable row in the picker. `id` is what gets persisted and shown —
 * for alias rows (e.g. Europe/Pristina) it differs from `canonical`, which is
 * the real IANA zone every Intl call resolves to.
 */
interface ZoneEntry {
  id: string;
  canonical: string;
}

/**
 * Topbar timezone picker. The trigger shows the *live* wall-clock in the
 * selected timezone — so every change reflects on screen immediately.
 * Popover surfaces the curated 26-zone list + Quick options (scheduler,
 * browser, UTC) and shows current time + DST status for fast comparison.
 */
export function TimezoneSelector() {
  const {
    timezone,
    displayTimezone,
    setTimezone,
    configTimezone,
    schedulerTimezone,
    browserTimezone,
    isUserOverride,
  } = useTimezone();
  const [open, setOpen] = useState(false);
  const now = useNow(1000); // tick once a second for snappy feedback

  const offsetLabel = useMemo(() => formatOffset(timezone, now), [timezone, now]);
  const clock = useMemo(() => formatClock(now, timezone), [timezone, now]);
  const dstActive = useMemo(() => isDstActiveNow(timezone, now), [timezone, now]);
  const longNow = useMemo(() => formatLongNow(now, timezone), [timezone, now]);

  // The browser zone shown/persisted for "Browser local" is locale-aware:
  // sq/…-XK locales on Europe/Belgrade get the Pristina alias.
  const browserDisplayId = useMemo(
    () => preferredDisplayId(browserTimezone),
    [browserTimezone]
  );

  // What clearing the override actually resolves to (mirrors the context's
  // resolution chain: dashboard config → scheduler → browser). The reset row
  // MUST be labeled with this zone — labeling it with the scheduler zone when
  // a dashboard SetTimeZone() wins would lie about what the click does.
  const defaultZone = configTimezone ?? schedulerTimezone ?? browserTimezone;
  const defaultDisplayId = useMemo(() => preferredDisplayId(defaultZone), [defaultZone]);
  const defaultSource = configTimezone ? "Dashboard default" : schedulerTimezone ? "Scheduler default" : "Browser local";
  // Offer the scheduler zone as its own explicit pick when it isn't already
  // what reset gives you (e.g. dashboard pinned to Tirane, scheduler machine
  // on Ljubljana).
  const schedulerDiffers =
    !!schedulerTimezone && canonicalizeTz(schedulerTimezone) !== canonicalizeTz(defaultZone);

  const all = useMemo(() => {
    const curated = listIanaTimezones();
    const canonical = canonicalizeTz(displayTimezone);
    return curated.includes(canonical) ? curated : [canonical, ...curated];
  }, [displayTimezone]);

  // Region groups, offset-sorted within each; aliased zones expand into one
  // entry per display id (Belgrade AND Pristina). Offsets only shift on DST
  // transitions, so this intentionally does NOT depend on the ticking clock.
  const groups = useMemo(() => {
    const at = new Date();
    const map = new Map<string, ZoneEntry[]>();
    const push = (entry: ZoneEntry) => {
      const r = regionOf(entry.id);
      const list = map.get(r);
      if (list) list.push(entry);
      else map.set(r, [entry]);
    };
    for (const tz of all) {
      push({ id: tz, canonical: tz });
      for (const alias of TZ_DISPLAY_ALIASES)
        if (alias.canonical === tz) push({ id: alias.displayId, canonical: tz });
    }
    for (const list of map.values())
      list.sort(
        (a, b) =>
          offsetMinutes(a.canonical, at) - offsetMinutes(b.canonical, at) ||
          a.id.localeCompare(b.id)
      );
    return REGION_ORDER.filter((r) => map.has(r)).map((r) => ({
      region: r,
      zones: map.get(r)!,
    }));
  }, [all]);

  function select(tz: string | null) {
    setTimezone(tz);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <button
          type="button"
          title={`${displayTimezone} · ${offsetLabel}${dstActive ? " (DST)" : ""}`}
          className={cn(
            "group h-7 inline-flex items-center gap-2 rounded-lg border border-border bg-transparent pl-2 pr-2.5 text-[11px] transition-colors",
            "hover:bg-surface-2 hover:text-foreground",
            isUserOverride ? "text-foreground" : "text-muted-foreground"
          )}
        >
          <Globe className="h-3 w-3 shrink-0 text-primary/70 group-hover:text-primary transition-colors" />
          <span className="font-medium tabular-nums leading-none">{clock}</span>
          <span className="text-muted-foreground/60">·</span>
          <span className="tabular-nums leading-none">{offsetLabel}</span>
          <span className="text-muted-foreground/40 leading-none">{shortLabel(displayTimezone)}</span>
        </button>
      </PopoverTrigger>
      <PopoverContent className="w-[360px] p-0" align="end" sideOffset={6}>
        {/* Status header — shows the currently selected timezone + live time */}
        <div className="px-3 pt-3 pb-2 border-b border-border bg-surface-0/40">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 text-[10px] uppercase tracking-wider text-muted-foreground/70">
              <Clock className="h-3 w-3" />
              Display timezone
            </div>
            {isUserOverride && (
              <button
                type="button"
                onClick={() => select(null)}
                title="Revert to the default timezone"
                className="inline-flex items-center gap-1 text-[10px] text-muted-foreground hover:text-foreground transition-colors"
              >
                <RotateCcw className="h-2.5 w-2.5" />
                Reset
              </button>
            )}
          </div>
          <div className="mt-1.5 flex items-baseline gap-2">
            <span className="font-mono text-[13px] font-semibold tracking-tight text-foreground">
              {shortLabel(displayTimezone)}
            </span>
            <span className="font-mono text-[10px] text-muted-foreground/70 truncate">
              {displayTimezone}
            </span>
          </div>
          <div className="mt-1 flex items-center gap-1.5 text-[11px] text-muted-foreground tabular-nums">
            <span>{longNow}</span>
            <span className="text-muted-foreground/40">·</span>
            <span>{offsetLabel}</span>
            {dstActive && (
              <span className="ml-0.5 rounded-sm bg-status-warning/15 text-status-warning px-1 py-px text-[9px] font-semibold tracking-wide uppercase">
                DST
              </span>
            )}
          </div>
        </div>

        <Command>
          <CommandInput placeholder="Search timezones…" className="h-9" />
          <CommandList className="max-h-[340px]">
            <CommandEmpty>No timezones match.</CommandEmpty>

            <CommandGroup heading="Quick">
              {/* Reset row — labeled with the zone reset actually resolves to. */}
              <CommandItem
                value={`__default__ ${defaultDisplayId}`}
                onSelect={() => select(null)}
                className="flex items-center gap-2"
              >
                <RotateCcw className="h-3.5 w-3.5 text-muted-foreground" />
                <span>{defaultSource}</span>
                <span className="ml-auto flex items-center gap-2 text-[11px] text-muted-foreground">
                  <span className="font-mono">{shortLabel(defaultDisplayId)}</span>
                  <span className="tabular-nums">{formatOffset(defaultZone, now)}</span>
                </span>
                {!isUserOverride && <Check className="h-3.5 w-3.5 text-primary shrink-0" />}
              </CommandItem>
              {schedulerDiffers && (
                <CommandItem
                  value={`__scheduler__ ${schedulerTimezone}`}
                  onSelect={() => select(preferredDisplayId(schedulerTimezone!))}
                  className="flex items-center gap-2"
                >
                  <Server className="h-3.5 w-3.5 text-muted-foreground" />
                  <span>Scheduler timezone</span>
                  <span className="ml-auto flex items-center gap-2 text-[11px] text-muted-foreground">
                    <span className="font-mono">{shortLabel(preferredDisplayId(schedulerTimezone!))}</span>
                    <span className="tabular-nums">{formatOffset(schedulerTimezone!, now)}</span>
                  </span>
                </CommandItem>
              )}
              <CommandItem
                value={`__local__ ${browserDisplayId}`}
                onSelect={() => select(browserDisplayId)}
                className="flex items-center gap-2"
              >
                <MapPin className="h-3.5 w-3.5 text-muted-foreground" />
                <span>Browser local</span>
                <span className="ml-auto flex items-center gap-2 text-[11px] text-muted-foreground">
                  <span className="font-mono">{shortLabel(browserDisplayId)}</span>
                  <span className="tabular-nums">{formatOffset(browserTimezone, now)}</span>
                </span>
              </CommandItem>
              <CommandItem
                value="__utc__ UTC"
                onSelect={() => select("UTC")}
                className="flex items-center gap-2"
              >
                <Globe className="h-3.5 w-3.5 text-muted-foreground" />
                <span>UTC</span>
                <span className="ml-auto text-[11px] text-muted-foreground tabular-nums">+00:00</span>
              </CommandItem>
            </CommandGroup>

            {groups.map((group) => (
              <div key={group.region}>
                <CommandSeparator />
                <CommandGroup heading={group.region}>
                  {group.zones.map((ent) => {
                    const active = displayTimezone === ent.id;
                    const dst = isDstActiveNow(ent.canonical, now);
                    return (
                      <CommandItem
                        key={ent.id}
                        // Label included so searching "berlin" or "pristina"
                        // matches even when it differs from the IANA id.
                        value={`${ent.id} ${shortLabel(ent.id)}`}
                        onSelect={() => select(ent.id)}
                        className={cn("flex items-center gap-2", active && "bg-primary/5")}
                      >
                        <div className="flex min-w-0 flex-1 flex-col">
                          <span
                            className={cn(
                              "truncate text-[12px] leading-tight",
                              active && "font-medium text-foreground"
                            )}
                          >
                            {shortLabel(ent.id)}
                          </span>
                          <span className="truncate font-mono text-[9.5px] leading-tight text-muted-foreground/50">
                            {ent.id}
                          </span>
                        </div>
                        {dst && (
                          <span className="rounded-sm bg-status-warning/15 text-status-warning px-1 py-px text-[9px] font-semibold tracking-wide uppercase">
                            DST
                          </span>
                        )}
                        <span className="w-11 text-right font-mono text-[11px] tabular-nums text-foreground/80">
                          {formatClock(now, ent.canonical)}
                        </span>
                        <span className="w-12 text-right text-[10px] tabular-nums text-muted-foreground">
                          {formatOffset(ent.canonical, now)}
                        </span>
                        {active ? (
                          <Check className="h-3.5 w-3.5 shrink-0 text-primary" />
                        ) : (
                          <span className="w-3.5 shrink-0" />
                        )}
                      </CommandItem>
                    );
                  })}
                </CommandGroup>
              </div>
            ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

// ──────────── helpers ────────────

/** Display order for the region groups in the picker. */
const REGION_ORDER = [
  "Universal",
  "Americas",
  "Atlantic",
  "Europe",
  "Africa",
  "Asia",
  "Australia & Pacific",
  "Other",
] as const;

function regionOf(tz: string): (typeof REGION_ORDER)[number] {
  if (tz === "UTC") return "Universal";
  switch (tz.split("/")[0]) {
    case "America":
      return "Americas";
    case "Atlantic":
      return "Atlantic";
    case "Europe":
      return "Europe";
    case "Africa":
      return "Africa";
    case "Asia":
      return "Asia";
    case "Australia":
    case "Pacific":
      return "Australia & Pacific";
    default:
      // Zones injected from outside the curated list (e.g. the browser's own
      // zone when it isn't curated) that don't match a known prefix.
      return "Other";
  }
}

/** Re-render hook ticking at the given interval so live clocks update on screen. */
function useNow(intervalMs: number): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);
  return now;
}

/**
 * "Los_Angeles" → "Los Angeles". Works for display-alias ids too
 * (Europe/Pristina → "Pristina") — the label IS the id's city segment, so no
 * separate label table is needed.
 */
function shortLabel(tz: string): string {
  const last = tz.split("/").pop() ?? tz;
  return last.replace(/_/g, " ");
}

/** Current UTC offset for a zone as `+02:00` / `-05:00`. Accepts alias ids. */
function formatOffset(tz: string, at: Date): string {
  try {
    const parts = new Intl.DateTimeFormat("en-US", {
      timeZone: canonicalizeTz(tz),
      timeZoneName: "longOffset",
    }).formatToParts(at);
    const off = parts.find((p) => p.type === "timeZoneName")?.value ?? "";
    if (off === "GMT" || off === "UTC") return "+00:00";
    return off.replace("GMT", "").replace("UTC", "");
  } catch {
    return "";
  }
}

/** Returns offset in minutes (e.g. +120 for CEST). */
function offsetMinutes(tz: string, at: Date): number {
  const s = formatOffset(tz, at);
  const m = s.match(/^([+-])(\d{2}):(\d{2})$/);
  if (!m) return 0;
  return (m[1] === "+" ? 1 : -1) * (parseInt(m[2], 10) * 60 + parseInt(m[3], 10));
}

/** True when the zone is currently observing daylight saving time. */
function isDstActiveNow(tz: string, now: Date): boolean {
  try {
    const year = now.getUTCFullYear();
    const winter = new Date(Date.UTC(year, 0, 15));
    const summer = new Date(Date.UTC(year, 6, 15));
    const winterOff = offsetMinutes(tz, winter);
    const summerOff = offsetMinutes(tz, summer);
    if (winterOff === summerOff) return false;
    const currentOff = offsetMinutes(tz, now);
    // DST is on when the current offset matches the larger (advanced) of the two
    // for northern hemisphere, and the smaller for southern. Easier check: it
    // doesn't equal the winter (standard) offset.
    return currentOff !== winterOff;
  } catch {
    return false;
  }
}

/** Compact "HH:MM" wall-clock for the trigger. Accepts alias ids. */
function formatClock(at: Date, tz: string): string {
  try {
    return new Intl.DateTimeFormat("en-GB", {
      timeZone: canonicalizeTz(tz),
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
    }).format(at);
  } catch {
    return "—";
  }
}

/** "Mon, Jun 8 · 14:23:45" for the popover header. Accepts alias ids. */
function formatLongNow(at: Date, tz: string): string {
  try {
    const fmt = new Intl.DateTimeFormat("en-US", {
      timeZone: canonicalizeTz(tz),
      weekday: "short",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
    });
    return fmt.format(at).replace(",", " ·");
  } catch {
    return "—";
  }
}
