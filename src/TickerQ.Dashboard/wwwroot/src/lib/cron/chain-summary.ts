import type { ChainExecutionSummaryDto } from "../../services/api-types";

export function isChainActive(summary?: ChainExecutionSummaryDto | null): boolean {
  return summary?.status === "Running" || summary?.status === "Waiting";
}
