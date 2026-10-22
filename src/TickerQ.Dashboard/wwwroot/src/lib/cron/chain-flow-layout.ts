import { isGroupedRunCondition } from "./run-condition.ts";

export interface ChainFlowLayoutNode {
  id: string;
  functionName?: string | null;
  runCondition?: string | null;
  children: ChainFlowLayoutNode[];
}

export interface ChainFlowPosition {
  id: string;
  x: number;
  y: number;
}

export interface ChainFlowEdge {
  fromId: string;
  toId: string;
}

export interface ChainFlowMerge {
  groupId: string;
  sourceIds: string[];
  targetIds: string[];
  anchorX: number;
  y: number;
}

export interface ChainFlowBounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface ChainFlowStage {
  groupId: string;
  branchRootIds: string[];
  bounds: ChainFlowBounds;
  outcomeBounds?: ChainFlowBounds;
}

export interface ChainFlowLayout {
  positions: ChainFlowPosition[];
  edges: ChainFlowEdge[];
  merges: ChainFlowMerge[];
  stages: ChainFlowStage[];
  width: number;
  height: number;
}

interface LayoutOptions<TNode extends ChainFlowLayoutNode> {
  nodeWidth: number;
  horizontalGap: number;
  verticalGap: number;
  nodeHeight: (node: TNode) => number;
}

interface MeasuredNode<TNode extends ChainFlowLayoutNode> {
  node: TNode;
  nodeHeight: number;
  width: number;
  height: number;
  kind: "normal" | "parallel";
  children: MeasuredNode<TNode>[];
  branches: MeasuredNode<TNode>[];
  continuations: MeasuredNode<TNode>[];
  branchHeight: number;
}

export const PARALLEL_GROUP_FUNCTION = "$tickerq.parallel-group";

/** Distributes branch inputs across the top edge of a merge marker. */
export function mergePortX(
  anchorX: number,
  index: number,
  count: number,
  span: number
) {
  if (count <= 1) return anchorX;
  return anchorX - span / 2 + (span * index) / (count - 1);
}

function rowWidth<TNode extends ChainFlowLayoutNode>(
  nodes: MeasuredNode<TNode>[],
  horizontalGap: number
) {
  if (nodes.length === 0) return 0;
  return nodes.reduce((sum, node) => sum + node.width, 0) +
    (nodes.length - 1) * horizontalGap;
}

/**
 * Lays out the persisted chain tree as a workflow. Parallel groups are special:
 * Group-marked children form concurrent lanes, while ordinary conditions are
 * final continuations placed after a shared merge rail.
 */
export function computeChainFlowLayout<TNode extends ChainFlowLayoutNode>(
  root: TNode,
  options: LayoutOptions<TNode>
): ChainFlowLayout {
  const { nodeWidth, horizontalGap, verticalGap, nodeHeight } = options;
  const parallelTopPadding = Math.max(10, verticalGap / 8);
  const parallelBottomPadding = verticalGap / 2;
  const outcomeGap = verticalGap / 6;
  const outcomeHeaderHeight = Math.max(44, verticalGap / 2);

  function measure(node: TNode): MeasuredNode<TNode> {
    const height = nodeHeight(node);
    const measuredChildren = node.children.map(child => measure(child as TNode));
    const isParallel = node.functionName === PARALLEL_GROUP_FUNCTION;
    const branches = isParallel
      ? measuredChildren.filter(child =>
          isGroupedRunCondition(child.node.runCondition))
      : [];
    const continuations = isParallel
      ? measuredChildren.filter(child =>
          !isGroupedRunCondition(child.node.runCondition))
      : [];

    if (isParallel && branches.length > 0) {
      const branchWidth = rowWidth(branches, horizontalGap);
      const continuationWidth = rowWidth(continuations, horizontalGap);
      const branchHeight = Math.max(...branches.map(branch => branch.height));
      const continuationHeight = continuations.length > 0
        ? Math.max(...continuations.map(continuation => continuation.height))
        : 0;
      return {
        node,
        nodeHeight: height,
        width: Math.max(
          nodeWidth,
          branchWidth + horizontalGap,
          continuationWidth > 0 ? continuationWidth + horizontalGap : 0
        ),
        height: parallelTopPadding + height + verticalGap + branchHeight +
          (continuations.length > 0
            ? parallelBottomPadding + outcomeGap + outcomeHeaderHeight + continuationHeight
            : parallelBottomPadding),
        kind: "parallel",
        children: measuredChildren,
        branches,
        continuations,
        branchHeight,
      };
    }

    const childrenWidth = rowWidth(measuredChildren, horizontalGap);
    return {
      node,
      nodeHeight: height,
      width: Math.max(nodeWidth, childrenWidth),
      height: height + (measuredChildren.length > 0
        ? verticalGap + Math.max(...measuredChildren.map(child => child.height))
        : 0),
      kind: "normal",
      children: measuredChildren,
      branches: [],
      continuations: [],
      branchHeight: 0,
    };
  }

  const positions: ChainFlowPosition[] = [];
  const edges: ChainFlowEdge[] = [];
  const merges: ChainFlowMerge[] = [];
  const stages: ChainFlowStage[] = [];

  function place(measured: MeasuredNode<TNode>, x: number, y: number): string[] {
    const positionedY = measured.kind === "parallel"
      ? y + parallelTopPadding
      : y;
    positions.push({
      id: measured.node.id,
      x: x + measured.width / 2 - nodeWidth / 2,
      y: positionedY,
    });

    if (measured.kind === "parallel") {
      const branchWidth = rowWidth(measured.branches, horizontalGap);
      let branchX = x + (measured.width - branchWidth) / 2;
      const branchY = positionedY + measured.nodeHeight + verticalGap;
      const branchExits: string[] = [];
      for (const branch of measured.branches) {
        edges.push({ fromId: measured.node.id, toId: branch.node.id });
        branchExits.push(...place(branch, branchX, branchY));
        branchX += branch.width + horizontalGap;
      }

      const stageX = x + (measured.width - branchWidth) / 2 - horizontalGap / 2;
      const stageWidth = branchWidth + horizontalGap;

      if (measured.continuations.length === 0) {
        const mergeY = branchY + measured.branchHeight + parallelBottomPadding;
        stages.push({
          groupId: measured.node.id,
          branchRootIds: measured.branches.map(branch => branch.node.id),
          bounds: {
            x: stageX,
            y,
            width: stageWidth,
            height: mergeY - y,
          },
        });
        // A parallel stage still has a synchronization point when no job follows
        // it. Emitting the descriptor keeps its footer visible and makes the
        // waiting/settled semantics explicit without drawing an outcome rail.
        merges.push({
          groupId: measured.node.id,
          sourceIds: branchExits,
          targetIds: [],
          anchorX: x + measured.width / 2,
          y: mergeY,
        });
        return branchExits;
      }

      const continuationWidth = rowWidth(measured.continuations, horizontalGap);
      let continuationX = x + (measured.width - continuationWidth) / 2;
      const outcomeX = continuationX - horizontalGap / 2;
      const mergeY = branchY + measured.branchHeight + parallelBottomPadding;
      const outcomeY = mergeY + outcomeGap;
      const continuationY = outcomeY + outcomeHeaderHeight;
      const continuationExits: string[] = [];
      for (const continuation of measured.continuations) {
        continuationExits.push(...place(continuation, continuationX, continuationY));
        continuationX += continuation.width + horizontalGap;
      }
      // Reserve the lower part of the parallel stage for its integrated
      // synchronization footer, then leave one clear connector to the
      // continuation section.
      stages.push({
        groupId: measured.node.id,
        branchRootIds: measured.branches.map(branch => branch.node.id),
        bounds: {
          x: stageX,
          y,
          width: stageWidth,
          height: mergeY - y,
        },
        outcomeBounds: {
          x: outcomeX,
          y: outcomeY,
          width: continuationWidth + horizontalGap,
          height: Math.max(...measured.continuations.map(continuation => continuation.height)) +
            outcomeHeaderHeight + verticalGap / 3,
        },
      });
      merges.push({
        groupId: measured.node.id,
        sourceIds: branchExits,
        targetIds: measured.continuations.map(continuation => continuation.node.id),
        anchorX: x + measured.width / 2,
        y: mergeY,
      });
      return continuationExits;
    }

    if (measured.children.length === 0)
      return [measured.node.id];

    const childrenWidth = rowWidth(measured.children, horizontalGap);
    let childX = x + (measured.width - childrenWidth) / 2;
    const childY = y + measured.nodeHeight + verticalGap;
    const exits: string[] = [];
    for (const child of measured.children) {
      edges.push({ fromId: measured.node.id, toId: child.node.id });
      exits.push(...place(child, childX, childY));
      childX += child.width + horizontalGap;
    }
    return exits;
  }

  const measuredRoot = measure(root);
  place(measuredRoot, 0, 0);
  return {
    positions,
    edges,
    merges,
    stages,
    width: measuredRoot.width,
    height: measuredRoot.height,
  };
}
