import { useEffect, useRef, useState } from "react";
import { parseUtc } from "@/lib/cron/format";

/**
 * Returns the set of row ids that should play the `.animate-row-highlight`
 * flash. A row lights up only when it (a) hasn't been seen before in this
 * mounted view and (b) was created within the last 30s — i.e. it genuinely
 * arrived while the user was watching, not just paged in. Each fresh batch
 * fades out after 5s on its own timer (matches the CSS animation length).
 *
 * Mirrors the Hub's row-highlight behaviour.
 */
export function useRowHighlight<T>(
  items: T[] | undefined,
  getId: (item: T) => string,
  getCreatedAt: (item: T) => string | null | undefined,
  resetKey?: string | number | null
): Set<string> {
  const seenRef = useRef<Set<string>>(new Set());
  const [highlighted, setHighlighted] = useState<Set<string>>(new Set());

  // Reset the seen set when the surrounding context changes (e.g. switching
  // to a different cron's occurrences) so stale-but-recent rows don't flash.
  useEffect(() => {
    seenRef.current = new Set();
    setHighlighted(new Set());
  }, [resetKey]);

  useEffect(() => {
    if (!items) return;
    const now = Date.now();
    const fresh: string[] = [];
    for (const item of items) {
      const id = getId(item);
      if (seenRef.current.has(id)) continue;
      seenRef.current.add(id);
      const created = getCreatedAt(item);
      const ms = created ? parseUtc(created).getTime() : NaN;
      if (Number.isFinite(ms) && now - ms < 30_000) fresh.push(id);
    }
    if (fresh.length === 0) return;

    setHighlighted((prev) => {
      const next = new Set(prev);
      for (const id of fresh) next.add(id);
      return next;
    });

    // Per-batch deactivation timer; not cleared on cleanup so a previous
    // batch keeps fading on its own clock when new data arrives mid-animation.
    window.setTimeout(() => {
      setHighlighted((prev) => {
        const next = new Set(prev);
        for (const id of fresh) next.delete(id);
        return next.size === prev.size ? prev : next;
      });
    }, 5000);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items]);

  return highlighted;
}
