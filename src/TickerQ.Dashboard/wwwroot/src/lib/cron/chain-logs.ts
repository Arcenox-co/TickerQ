import type { TickerLogBatchItem, TickerLogTailResponse, TickerLogLine, ExecutionLogHealth, TimeTickerFlatDto } from "../../services/api-types";

const lineKey = (line: TickerLogLine) => line.id ?? `${line.unixMs}|${line.category}|${line.message}`;

const ACTIVE = new Set(["Idle", "Queued", "InProgress"]);
export const LOG_BATCH_SIZE = 50;
export const CHAIN_LOG_LINE_LIMIT = 1_000;
const CHAIN_LOG_BYTE_LIMIT = 1024 * 1024;
export interface ChainLogEntry {
  cursor: string;
  tail: TickerLogTailResponse;
  status: string;
  remaining: number;
}
export interface ChainLogState {
  entries: Record<string, ChainLogEntry>;
  displayTruncated: boolean;
  writeHealth?: ExecutionLogHealth;
}
export function needsChainLogRefresh(list: TimeTickerFlatDto[], previous?: ChainLogState): boolean {
  return list.some(t => ACTIVE.has(t.status) || !previous?.entries[t.id]
    || previous.entries[t.id].status !== t.status || previous.entries[t.id].remaining > 0);
}

export async function fetchChainLogs(
  list: TimeTickerFlatDto[], previous: ChainLogState | undefined,
  fetchBatch: (items: { tickerId: string; cursor?: string }[]) => Promise<{ items: TickerLogBatchItem[] }>,
  force = false,
): Promise<ChainLogState> {
  const entries: Record<string, ChainLogEntry> = {};
  for (const t of list) if (previous?.entries[t.id]) entries[t.id] = previous.entries[t.id];
  const selected = list.filter(t => force || ACTIVE.has(t.status) || !entries[t.id]
    || entries[t.id].status !== t.status || entries[t.id].remaining > 0);
  let writeHealth = previous?.writeHealth;
  let displayTruncated = previous?.displayTruncated === true;
  // Sequential batches bound both requests in flight and response memory.
  for (let offset = 0; offset < selected.length; offset += LOG_BATCH_SIZE) {
    const batch = selected.slice(offset, offset + LOG_BATCH_SIZE);
    const response = await fetchBatch(batch.map(t => ({ tickerId: t.id, cursor: entries[t.id]?.cursor })));
    for (const t of batch) {
      const item = response.items.find(x => x.tickerId === t.id);
      if (!item) throw new Error("The log batch response is incomplete.");
      writeHealth = item.tail.writeHealth;
      const old = entries[t.id];
      const remaining = force || !old || old.status !== t.status ? 4 : old.remaining;
      const lines = item.reset ? item.tail.lines : [...(old?.tail.lines ?? []), ...item.tail.lines];
      entries[t.id] = { cursor: item.cursor, status: t.status,
        remaining: ACTIVE.has(t.status) ? 4 : Math.max(0, remaining - 1),
        tail: { ...item.tail, lines: [...new Map(lines.map(line => [lineKey(line), line])).values()] } };
    }
    displayTruncated = trimChainLogs(entries) || displayTruncated;
  }
  return { entries, displayTruncated, writeHealth };
}

function trimChainLogs(entries: Record<string, ChainLogEntry>): boolean {
  // A large chain must not accumulate every job's complete tail in the browser.
  const ordered = Object.entries(entries).flatMap(([id, entry]) => entry.tail.lines.map(line => ({ id, line })))
    .sort((a, b) => b.line.unixMs - a.line.unixMs || lineKey(b.line).localeCompare(lineKey(a.line)));
  const keep = new Set<string>();
  const encoder = new TextEncoder();
  let bytes = 0;
  for (const { id, line } of ordered) {
    const size = encoder.encode(line.message + line.category + line.functionName).byteLength + 100;
    if (keep.size >= CHAIN_LOG_LINE_LIMIT || bytes + size > CHAIN_LOG_BYTE_LIMIT) break;
    bytes += size; keep.add(id + ":" + lineKey(line));
  }
  for (const [id, entry] of Object.entries(entries)) {
    entries[id] = { ...entry, tail: { ...entry.tail, lines: entry.tail.lines.filter(line => keep.has(id + ":" + lineKey(line))) } };
  }
  return keep.size < ordered.length;
}
