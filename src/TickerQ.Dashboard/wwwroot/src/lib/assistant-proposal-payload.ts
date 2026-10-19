import type { ProposalChainNode } from "../services/assistant-api.ts";
import type { RunCondition, TimeTickerNode } from "../services/api-types.ts";
import { encodeRequestPayload } from "./request-payload.ts";

/** Map assistant proposal JSON into the same UTF-8 Base64 wire contract as manual creates. */
export function proposalChainToTimeTickerNode(
  node: ProposalChainNode,
  isRoot: boolean,
): TimeTickerNode {
  return {
    function: node.function,
    description: node.description || null,
    retries: node.retries ?? null,
    request: encodeRequestPayload(node.requestJson),
    retryIntervalsSeconds: null,
    runCondition: isRoot ? null : ((node.runCondition ?? "OnSuccess") as RunCondition),
    children: (node.children ?? []).map((child) => proposalChainToTimeTickerNode(child, false)),
  };
}

/** Cron definitions store children separately; the root remains the cron function. */
export function proposalChainToCronTemplate(node: ProposalChainNode): import("../services/api-types.ts").CronChainTemplate {
  const child = (step: ProposalChainNode): import("../services/api-types.ts").CronChainNode => ({
    function: step.function,
    description: step.description || null,
    retries: step.retries ?? 0,
    request: encodeRequestPayload(step.requestJson),
    runCondition: step.runCondition as RunCondition,
    onStale: "Restart",
    children: (step.children ?? []).map(child),
  });
  if (!node.children?.length) throw new Error("A recurring chain requires child steps.");
  return { deleteBehavior: "Restrict", children: node.children.map(child) };
}
