export const ROW_HIGHLIGHT_WINDOW_MS = 30_000;
export const MAX_SEEN_ROW_IDS = 2_048;

/** Only recently created ids can flash; older ids need no retained history. */
export function rememberRecentRows<T>(
  seen: Map<string, number>, items: T[], getId: (item: T) => string,
  getCreatedMs: (item: T) => number, now: number,
): string[] {
  for (const [id, expiresAt] of seen) if (expiresAt <= now) seen.delete(id);
  const fresh: string[] = [];
  for (const item of items) {
    const id = getId(item);
    const created = getCreatedMs(item);
    if (!Number.isFinite(created) || created > now || now - created >= ROW_HIGHLIGHT_WINDOW_MS || seen.has(id)) continue;
    seen.set(id, created + ROW_HIGHLIGHT_WINDOW_MS);
    fresh.push(id);
    while (seen.size > MAX_SEEN_ROW_IDS) seen.delete(seen.keys().next().value!);
  }
  return fresh.slice(-MAX_SEEN_ROW_IDS);
}
