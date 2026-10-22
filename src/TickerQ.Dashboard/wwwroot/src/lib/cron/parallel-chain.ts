import type { ChainNode } from "./chain-node.ts";
import {
  isGroupedRunCondition,
  type BaseRunCondition,
} from "./run-condition.ts";
import { PARALLEL_GROUP_FUNCTION } from "./chain-flow-layout.ts";

export type ParallelChildRole = "branch" | "outcome";

/** Group-prefixed conditions identify grouped jobs independently of their rule. */
export function parallelChildRole(
  parent: Pick<ChainNode, "functionName"> | null | undefined,
  child: Pick<ChainNode, "runCondition">
): ParallelChildRole | null {
  if (parent?.functionName !== PARALLEL_GROUP_FUNCTION) return null;
  return isGroupedRunCondition(child.runCondition) ? "branch" : "outcome";
}

export const PARALLEL_OUTCOME_CONDITIONS: readonly BaseRunCondition[] = [
  "OnSuccess",
  "OnFailure",
  "OnCancelled",
  "OnFailureOrCancelled",
  "OnAnyCompletedStatus",
  "OnAnyTerminalStatus",
];

/** Conditions available to a grouped job. The editor converts them to durable
 * group-prefixed values so changing a rule never changes membership. */
export const GROUP_CHILD_CONDITIONS: readonly BaseRunCondition[] = [
  "InProgress",
  ...PARALLEL_OUTCOME_CONDITIONS,
];

/**
 * Keeps the dashboard from submitting a structural coordinator that has no
 * grouped job. Final jobs are optional because a group may be the final stage
 * in a chain. Group membership and the selected condition are separate.
 */
export function validateParallelGroups(node: ChainNode): string | null {
  if (node.active && node.functionName === PARALLEL_GROUP_FUNCTION) {
    const activeChildren = node.children.filter(child => child.active);
    const branchCount = activeChildren.filter(
      child => isGroupedRunCondition(child.runCondition)
    ).length;

    if (branchCount === 0)
      return "Every group needs at least one grouped job";
  }

  for (const child of node.children) {
    const error = validateParallelGroups(child);
    if (error) return error;
  }

  return null;
}
