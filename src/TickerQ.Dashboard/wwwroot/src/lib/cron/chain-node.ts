import type { RunCondition } from "./run-condition";
import { parseIntervalsList } from "./status-config.ts";
import { parseUtc } from "./format.ts";
import { decodeRequestPayload, encodeRequestPayload } from "../request-payload.ts";
import type { ChainDeleteBehavior, StaleAction, TimeTickerFlatDto, TimeTickerNode } from "../../services/api-types";

// ── Editor state for a single chain node ──
// Everything here is user-editable and must round-trip through the flat DTO
// (read) and the request node (write) without losing any replaceable field.
export interface ChainNode {
  deleteBehavior?: ChainDeleteBehavior;
  id: string;
  functionName: string;
  runCondition: RunCondition;
  retries: number;
  retryIntervalsSeconds: string;
  description: string;
  requestJson: string;
  requestValid: boolean;
  onStale: StaleAction;
  timeoutSeconds: number;
  active: boolean;
  scheduledAt: string;
  children: ChainNode[];
}

// Convert a server ISO instant into the value a <input type="datetime-local">
// expects (local wall-clock, second precision, no timezone suffix).
export function toDatetimeLocal(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = parseUtc(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(
    d.getHours()
  )}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

// Inverse of toDatetimeLocal for submit: a local datetime-local string back to
// an ISO instant. Empty means "fire at submit time" and is the caller's concern.
export function scheduledLocalToIso(scheduledAt: string): string {
  if (!scheduledAt) return "";
  return new Date(scheduledAt).toISOString();
}

/**
 * Convert a flat chain (root + descendants returned by useChainTickers) into
 * the builder's ChainNode tree. Every node gets a new `edit-<id>` id so the
 * builder treats them as fresh — the existing chain is fully replaced on
 * submit through the server-side atomic replacement endpoint.
 */
export function tickersToChainNode(
  tickers: TimeTickerFlatDto[],
  rootId: string,
  payloadMap: Map<string, string>
): ChainNode | null {
  const childrenByParent = new Map<string, TimeTickerFlatDto[]>();
  for (const t of tickers) {
    if (t.parentId) {
      const arr = childrenByParent.get(t.parentId) ?? [];
      arr.push(t);
      childrenByParent.set(t.parentId, arr);
    }
  }
  const byId = new Map(tickers.map((t) => [t.id, t]));
  const root = byId.get(rootId);
  if (!root) return null;
  const build = (t: TimeTickerFlatDto, isRoot: boolean): ChainNode => ({
    id: `edit-${t.id}`,
    functionName: t.functionName,
    runCondition: t.runCondition ?? "OnAnyCompletedStatus",
    retries: t.retries ?? 0,
    retryIntervalsSeconds: t.retryIntervalsSeconds?.join(", ") ?? "",
    description: t.description ?? "",
    requestJson: payloadMap.get(t.id) ?? "",
    requestValid: true,
    onStale: t.onStale ?? "Restart",
    deleteBehavior: isRoot ? (t.deleteBehavior ?? "Cascade") : "Cascade",
    timeoutSeconds: t.timeoutSeconds ?? 0,
    active: true,
    scheduledAt: isRoot ? toDatetimeLocal(t.scheduledFor) : "",
    children: (childrenByParent.get(t.id) ?? []).map((c) => build(c, false)),
  });
  return build(root, true);
}

// Serialize an editor node (and its active subtree) into the request wire shape.
export function chainNodeToRequest(node: ChainNode): TimeTickerNode {
  return {
    function: node.functionName,
    description: node.description || null,
    retries: node.retries > 0 ? node.retries : null,
    retryIntervalsSeconds: node.retryIntervalsSeconds.trim()
      ? parseIntervalsList(node.retryIntervalsSeconds)
      : null,
    request: encodeRequestPayload(node.requestJson),
    runCondition: node.runCondition,
    onStale: node.onStale,
    deleteBehavior: node.deleteBehavior ?? "Cascade",
    timeoutSeconds: node.timeoutSeconds > 0 ? node.timeoutSeconds : null,
    children: node.children.filter((c) => c.active).map(chainNodeToRequest),
  };
}

/** Hydrate a saved template/draft into fresh editor state. Saved definitions
 * contain configuration only, so every canvas id is regenerated. */
export function requestToChainNode(
  node: TimeTickerNode,
  scheduledAt = "",
  makeId: () => string = () => `saved-${Math.random().toString(36).slice(2, 9)}`
): ChainNode {
  const build = (item: TimeTickerNode, isRoot: boolean): ChainNode => ({
    id: makeId(),
    functionName: item.function,
    description: item.description ?? "",
    requestJson: item.request ? decodeRequestPayload(item.request) : "",
    requestValid: true,
    retries: item.retries ?? 0,
    retryIntervalsSeconds: item.retryIntervalsSeconds?.join(", ") ?? "",
    runCondition: item.runCondition ?? "OnSuccess",
    onStale: item.onStale ?? "Restart",
    deleteBehavior: isRoot ? (item.deleteBehavior ?? "Cascade") : "Cascade",
    timeoutSeconds: item.timeoutSeconds ?? 0,
    active: true,
    scheduledAt: isRoot ? scheduledAt : "",
    children: (item.children ?? []).map(child => build(child, false)),
  });
  return build(node, true);
}
