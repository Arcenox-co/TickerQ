import type { CronChainNode, CronChainTemplate, CronOccurrenceFlatDto, CronTickerFlatDto, TimeTickerFlatDto, TimeTickerNode } from "../../services/api-types";
import { decodeRequestPayload } from "../request-payload.ts";
import { chainNodeToRequest, type ChainNode } from "./chain-node.ts";

export function canViewOccurrenceChain(cron: CronTickerFlatDto | undefined, occurrence: CronOccurrenceFlatDto): boolean {
  return !!occurrence.chainRootId || !!(cron?.chain && cron.id === occurrence.cronTickerId &&
    ["Idle", "Queued", "InProgress"].includes(occurrence.status));
}

/** Display-only steps until the occurrence publishes its actual chain. Never persist these IDs. */
export function cronOccurrenceChainPreview(cron: CronTickerFlatDto, occurrence: CronOccurrenceFlatDto): TimeTickerFlatDto[] {
  if (!cron.chain || occurrence.chainRootId || !canViewOccurrenceChain(cron, occurrence)) return [];
  const list: TimeTickerFlatDto[] = [];
  const add = (node: CronChainNode, id: string, parentId: string | null) => {
    list.push({
      id, parentId, originCronTickerId: cron.id, functionName: node.function,
      status: "Idle", scheduledFor: parentId ? null : occurrence.scheduledFor,
      elapsedTime: 0, retries: node.retries, retryCount: 0, priority: parentId ? "Normal" : cron.priority,
      createdAt: occurrence.createdAt, childCount: node.children?.length ?? 0,
      exceptionMessage: null, skippedReason: null, runCondition: parentId ? node.runCondition : null,
      executedAt: null, description: node.description ?? null, lockHolder: null, lockedAt: null,
      progressPercent: null, progressMessage: null, progressUpdatedAt: null,
      retryIntervalsSeconds: node.retryIntervals ?? null, onStale: node.onStale,
      timeoutSeconds: node.timeoutSeconds ?? null, deleteBehavior: parentId ? "Cascade" : cron.chain!.deleteBehavior,
    });
    node.children?.forEach((child, index) => add(child, `${id}/step-${index}`, id));
  };
  add({ function: cron.functionName, description: cron.description, retries: cron.retries,
    retryIntervals: cron.retryIntervalsSeconds, onStale: cron.onStale, timeoutSeconds: cron.timeoutSeconds,
    runCondition: "OnSuccess", children: cron.chain.children }, occurrence.id, null);
  return list;
}

export function cronToChainNode(cron: CronTickerFlatDto, request: string | null): ChainNode {
  const build = (node: CronChainNode, path: string): ChainNode => ({
    id: `cron-${path}`, functionName: node.function, description: node.description ?? "",
    requestJson: node.request ? decodeRequestPayload(node.request) : "", requestValid: true,
    retries: node.retries, retryIntervalsSeconds: node.retryIntervals?.join(", ") ?? "",
    runCondition: node.runCondition, onStale: node.onStale, timeoutSeconds: node.timeoutSeconds ?? 0,
    deleteBehavior: "Cascade", active: true, scheduledAt: "",
    children: (node.children ?? []).map((child, i) => build(child, `${path}-${i}`)),
  });
  return {
    ...build({ function: cron.functionName, description: cron.description, request,
      retries: cron.retries, retryIntervals: cron.retryIntervalsSeconds,
      runCondition: "OnSuccess", onStale: cron.onStale, timeoutSeconds: cron.timeoutSeconds,
      children: cron.chain?.children ?? [] }, "root"),
    deleteBehavior: cron.chain?.deleteBehavior ?? "Restrict",
  };
}

export function chainNodeToCronTemplate(root: ChainNode): CronChainTemplate {
  const build = (node: TimeTickerNode): CronChainNode => ({
    function: node.function, description: node.description, request: node.request,
    retries: node.retries ?? 0, retryIntervals: node.retryIntervalsSeconds,
    runCondition: node.runCondition ?? "OnSuccess", onStale: node.onStale ?? "Restart",
    timeoutSeconds: node.timeoutSeconds, children: (node.children ?? []).map(build),
  });
  return { deleteBehavior: root.deleteBehavior ?? "Restrict",
    children: (chainNodeToRequest(root).children ?? []).map(build) };
}
