import { PARALLEL_GROUP_FUNCTION } from "./chain-flow-layout.ts";
import { isGroupedRunCondition } from "./run-condition.ts";

export interface RetryImpactNode {
  id: string;
  functionName: string;
  runCondition?: string | null;
  children: RetryImpactNode[];
}

export interface RetryImpact {
  selectedJobName: string;
  affectedJobIds: string[];
  affectedJobNames: string[];
  downstreamJobCount: number;
  additionalOutcomeJobCount: number;
  recalculatedGroupCount: number;
  reusedGroupedBranchCount: number;
}

/**
 * Mirrors the durable chain retry planner closely enough to explain a retry
 * before it is submitted. Structural group coordinators are excluded from the
 * user-facing job counts, but their outcome recalculation is reported.
 */
export function computeRetryImpact(
  root: RetryImpactNode,
  selectedId: string
): RetryImpact | null {
  const byId = new Map<string, RetryImpactNode>();
  const parentById = new Map<string, string>();

  const collect = (node: RetryImpactNode, parent?: RetryImpactNode) => {
    byId.set(node.id, node);
    if (parent) parentById.set(node.id, parent.id);
    node.children.forEach(child => collect(child, node));
  };
  collect(root);

  const selected = byId.get(selectedId);
  if (!selected) return null;

  const path: string[] = [];
  for (let current: string | undefined = selectedId; current; current = parentById.get(current))
    path.push(current);
  path.reverse();
  const pathSet = new Set(path);

  const selectedSubtree = new Set<string>();
  addSubtree(selected, selectedSubtree);
  const affected = new Set(selectedSubtree);
  const recalculatedGroups = new Set<string>();
  let reusedGroupedBranchCount = 0;

  for (let index = 0; index < path.length; index++) {
    const node = byId.get(path[index]);
    if (node?.functionName !== PARALLEL_GROUP_FUNCTION) continue;

    const pathChild = index + 1 < path.length
      ? byId.get(path[index + 1])
      : undefined;

    // A selected outcome is already downstream of the settled aggregate. It
    // does not cause the group or its sibling outcomes to replay.
    if (pathChild && !isGroupedRunCondition(pathChild.runCondition)) continue;

    recalculatedGroups.add(node.id);
    affected.add(node.id);
    for (const child of node.children) {
      if (pathSet.has(child.id)) continue;
      if (isGroupedRunCondition(child.runCondition)) {
        if (!affected.has(child.id)) reusedGroupedBranchCount++;
        continue;
      }
      addSubtree(child, affected);
    }
  }

  const isJob = (id: string) =>
    byId.get(id)?.functionName !== PARALLEL_GROUP_FUNCTION;
  const selectedSubtreeJobs = [...selectedSubtree].filter(isJob);
  const affectedJobIds = [...affected].filter(isJob);
  const additionalOutcomeJobCount = affectedJobIds.filter(
    id => !selectedSubtree.has(id)
  ).length;

  return {
    selectedJobName: selected.functionName,
    affectedJobIds,
    affectedJobNames: affectedJobIds.map(id => byId.get(id)?.functionName ?? id),
    downstreamJobCount: Math.max(0, selectedSubtreeJobs.length - (isJob(selectedId) ? 1 : 0)),
    additionalOutcomeJobCount,
    recalculatedGroupCount: recalculatedGroups.size,
    reusedGroupedBranchCount,
  };
}

function addSubtree(node: RetryImpactNode, ids: Set<string>) {
  ids.add(node.id);
  node.children.forEach(child => addSubtree(child, ids));
}
