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
