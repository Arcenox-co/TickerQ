export const CHAIN_MAX_DEPTH = 20;
export const CHAIN_MAX_NODES = 500;

export function assertChainLevelFits(currentCount: number, nextCount: number): void {
  if (currentCount + nextCount > CHAIN_MAX_NODES) {
    throw new Error(
      `Chain contains more than ${CHAIN_MAX_NODES} nodes and cannot be edited safely.`,
    );
  }
}

export function assertChainTraversalComplete(frontierCount: number): void {
  if (frontierCount > 0) {
    throw new Error(
      `Chain exceeds the safe traversal depth of ${CHAIN_MAX_DEPTH} and cannot be edited safely.`,
    );
  }
}
