import type { TimeTickerFlatDto } from "../../services/api-types";

const terminalStatuses = new Set(["Done", "DueDone", "Failed", "Cancelled", "Skipped"]);

// Call with the complete chain response, including descendants at every depth.
export function isChainDeletionRestricted(root: TimeTickerFlatDto, chain: TimeTickerFlatDto[]): boolean {
  return root.deleteBehavior === "Restrict" && chain.some(
    node => node.id !== root.id && !terminalStatuses.has(node.status),
  );
}
