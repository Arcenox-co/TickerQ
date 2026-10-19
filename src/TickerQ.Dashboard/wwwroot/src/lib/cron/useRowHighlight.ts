import { useEffect, useRef, useState } from "react";
import { parseUtc } from "@/lib/cron/format";
import { MAX_SEEN_ROW_IDS, rememberRecentRows } from "./row-highlight-cache";

/** Flash recently created rows once, with bounded recent-id memory and one timer. */
export function useRowHighlight<T>(
  items: T[] | undefined,
  getId: (item: T) => string,
  getCreatedAt: (item: T) => string | null | undefined,
  resetKey?: string | number | null
): Set<string> {
  const seenRef = useRef(new Map<string, number>());
  const activeRef = useRef(new Map<string, number>());
  const timerRef = useRef<ReturnType<typeof window.setTimeout> | undefined>(undefined);
  const [highlighted, setHighlighted] = useState<Set<string>>(new Set());

  useEffect(() => {
    seenRef.current.clear();
    activeRef.current.clear();
    setHighlighted(new Set());
    return () => {
      window.clearTimeout(timerRef.current);
      seenRef.current.clear();
      activeRef.current.clear();
    };
  }, [resetKey]);

  useEffect(() => {
    if (!items) return;
    const now = Date.now();
    const fresh = rememberRecentRows(seenRef.current, items, getId, (item) => {
      const created = getCreatedAt(item);
      return created ? parseUtc(created).getTime() : NaN;
    }, now);
    for (const id of fresh) activeRef.current.set(id, now + 5_000);
    while (activeRef.current.size > MAX_SEEN_ROW_IDS)
      activeRef.current.delete(activeRef.current.keys().next().value!);

    function expire() {
      window.clearTimeout(timerRef.current);
      const current = Date.now();
      let nextExpiry = Infinity;
      for (const [id, expiresAt] of activeRef.current) {
        if (expiresAt <= current) activeRef.current.delete(id);
        else nextExpiry = Math.min(nextExpiry, expiresAt);
      }
      setHighlighted(new Set(activeRef.current.keys()));
      if (Number.isFinite(nextExpiry)) timerRef.current = window.setTimeout(expire, nextExpiry - current);
    }
    expire();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items, resetKey]);

  return highlighted;
}
