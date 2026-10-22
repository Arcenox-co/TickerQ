import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { ChainDeleteBehaviorControl } from "@/components/cron/ChainDeleteBehaviorControl";
import { isChainDeletionRestricted } from "@/lib/cron/chain-deletion";
import {
  ArrowLeft,
  Check,
  Clock,
  Copy,
  CornerUpLeft,
  GitBranch,
  GitMerge,
  Loader2,
  Maximize2,
  Pencil,
  RotateCcw,
  Timer,
} from "lucide-react";

import { StatusBadge } from "@/components/cron/StatusBadges";
import { ConditionEvaluationBadge } from "@/components/cron/RunConditionBadge";
import { LogsPanel } from "@/components/cron/LogsPanel";
import { ChainLogsPanel } from "@/components/cron/ChainLogsPanel";
import { CancelTickerButton } from "@/components/cron/CancelTickerButton";
import { LockHolderValue } from "@/components/cron/LockHolderValue";
import { JobPayloadDetails } from "@/components/cron/JobPayloadDetails";
import { resolveChainLockHolder } from "@/lib/cron/lock-holder";
import { Button } from "@/components/ui/button";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogMedia,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { useChainTickers, useRetryTimeTickerFromHere } from "@/services/hooks";
import {
  aggregateBranchSubtreeStatus,
  isBranchSubtreeSettled,
  STATUS_CONFIG,
  summarizeBranchStatuses,
} from "@/lib/cron/status-config";
import {
  baseRunCondition,
  conditionEvaluationState,
  isGroupedRunCondition,
  runConditionHsl,
} from "@/lib/cron/run-condition";
import {
  computeChainFlowLayout,
  PARALLEL_GROUP_FUNCTION,
  type ChainFlowPosition,
  type ChainFlowLayoutNode,
} from "@/lib/cron/chain-flow-layout";
import { formatAbsolute, formatDuration, relativeTime } from "@/lib/cron/format";
import { computeRetryImpact } from "@/lib/cron/retry-impact";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import type { TimeTickerFlatDto } from "@/services/api-types";

// Read-only mirror of the chain builder canvas — port of the Hub's
// ChainFlowchartView. Renders the parent + descendants tree with each node's
// live status (color-coded), run-condition paths and pills, and a resizable
// merged logs panel pinned to the bottom.

interface ChainTreeNode extends ChainFlowLayoutNode {
  id: string;
  functionName: string;
  runCondition: string | null;
  ticker: TimeTickerFlatDto;
  children: ChainTreeNode[];
}

const NODE_W = 300;
const NODE_H = 164;
const GROUP_HEADER_H = 58;
const H_GAP = 64;
const V_GAP = 96;
const MIN_ZOOM = 0.15;
const DETAIL_RAIL_WIDTH = 320;
const CONNECTOR_COLOR = "hsl(var(--muted-foreground) / 0.42)";
const JOIN_PATH_COLOR = "hsl(var(--workflow-sync) / 0.82)";

function describeGroupStatus(status: TimeTickerFlatDto["status"]): string {
  switch (status) {
    case "Idle":
    case "Queued":
      return "The group has not started coordinating its matching jobs yet.";
    case "InProgress":
      return "Waiting for every started grouped job and descendant to settle.";
    case "Done":
    case "DueDone":
      return "Every started grouped job and descendant settled successfully.";
    case "Failed":
      return "At least one grouped job or descendant failed.";
    case "Cancelled":
      return "At least one grouped job or descendant was cancelled.";
    case "Skipped":
      return "The group settled with a skipped grouped path.";
  }
}

function groupStatusLabel(status: TimeTickerFlatDto["status"]): string | undefined {
  return status === "Done" || status === "DueDone"
    ? "Completed"
    : undefined;
}

function isSuccessfulStatus(status: TimeTickerFlatDto["status"]): boolean {
  return status === "Done" || status === "DueDone";
}

function layoutJoinSummary(
  items: ReturnType<typeof summarizeBranchStatuses>
) {
  const itemGap = 9;
  const measured = items.map(item => ({
    ...item,
    text: `${item.count} ${item.label}`,
    width: 10 + `${item.count} ${item.label}`.length * 4.35,
  }));
  const totalWidth = measured.reduce((sum, item) => sum + item.width, 0) +
    Math.max(0, measured.length - 1) * itemGap;
  let cursor = -totalWidth / 2;
  return measured.map(item => {
    const positioned = { ...item, x: cursor };
    cursor += item.width + itemGap;
    return positioned;
  });
}

function collectBranchStatuses(node: ChainTreeNode): TimeTickerFlatDto["status"][] {
  return [
    node.ticker.status,
    ...node.children.flatMap(collectBranchStatuses),
  ];
}

function buildTreeFromFlat(
  list: TimeTickerFlatDto[],
  rootId: string
): ChainTreeNode | null {
  const byId = new Map(list.map((t) => [t.id, t]));
  const childrenOf = new Map<string, TimeTickerFlatDto[]>();
  for (const t of list) {
    if (t.parentId) {
      const arr = childrenOf.get(t.parentId) ?? [];
      arr.push(t);
      childrenOf.set(t.parentId, arr);
    }
  }
  const root = byId.get(rootId);
  if (!root) return null;
  const build = (t: TimeTickerFlatDto): ChainTreeNode => ({
    id: t.id,
    functionName: t.functionName,
    runCondition: t.runCondition,
    ticker: t,
    children: (childrenOf.get(t.id) ?? []).map(build),
  });
  return build(root);
}

export interface ChainFlowchartViewProps {
  rootId: string;
  /** Where the Back button returns to (label is derived). */
  from?: "time-tickers" | "cron-tickers" | "executions" | null;
  onBack: () => void;
  onEditChain?: (rootId: string) => void;
  /** Read-only cron template while waiting for real job IDs and execution data. */
  previewTickers?: TimeTickerFlatDto[];
  /** Memory-only execution supplied by the chain builder. Enables live logs but no mutations. */
  ephemeralTest?: boolean;
  ephemeralTestCompleted?: boolean;
  backLabel?: string;
}

export function ChainFlowchartView({
  rootId,
  from,
  onBack,
  onEditChain,
  previewTickers,
  ephemeralTest = false,
  ephemeralTestCompleted = false,
  backLabel: suppliedBackLabel,
}: ChainFlowchartViewProps) {
  const navigate = useNavigate();
  const backLabel = suppliedBackLabel ?? (from === "cron-tickers" ? "Back to occurrences" : from === "executions" ? "Back to Executions" : "Back to list");

  const containerRef = useRef<HTMLDivElement>(null);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [isPanning, setIsPanning] = useState(false);
  const [panStart, setPanStart] = useState({ x: 0, y: 0 });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [retryDialogOpen, setRetryDialogOpen] = useState(false);
  const retryMutation = useRetryTimeTickerFromHere();

  // Bottom logs panel — height is user-draggable so customers can give logs
  // more or less of the viewport depending on how much they're debugging.
  const [logsPanelHeight, setLogsPanelHeight] = useState(280);
  const handleLogsResizeStart = useCallback(
    (e: React.MouseEvent) => {
      const startY = e.clientY;
      const startHeight = logsPanelHeight;
      const onMove = (moveE: MouseEvent) => {
        const delta = startY - moveE.clientY;
        setLogsPanelHeight(Math.max(150, Math.min(800, startHeight + delta)));
      };
      const onUp = () => {
        document.removeEventListener("mousemove", onMove);
        document.removeEventListener("mouseup", onUp);
      };
      document.addEventListener("mousemove", onMove);
      document.addEventListener("mouseup", onUp);
    },
    [logsPanelHeight]
  );

  const { timezone } = useTimezone();
  const hasExternalTickers = previewTickers !== undefined;
  const isPreview = hasExternalTickers && !ephemeralTest;
  const isReadOnly = isPreview || ephemeralTest;
  const chainQuery = useChainTickers(hasExternalTickers ? null : rootId);
  const list = useMemo(
    () => previewTickers ?? chainQuery.data ?? [],
    [previewTickers, chainQuery.data]
  );

  const { positions, edges, merges, stages, nodeMap, parentMap, maxX, maxY } = useMemo(() => {
    const tree = list.length ? buildTreeFromFlat(list, rootId) : null;
    if (!tree) {
      return {
        positions: [] as ChainFlowPosition[],
        edges: [],
        merges: [],
        stages: [],
        nodeMap: new Map<string, ChainTreeNode>(),
        parentMap: new Map<string, ChainTreeNode>(),
        maxX: 600,
        maxY: 400,
      };
    }
    const layout = computeChainFlowLayout(tree, {
      nodeWidth: NODE_W,
      horizontalGap: H_GAP,
      verticalGap: V_GAP,
      nodeHeight: node =>
        node.functionName === PARALLEL_GROUP_FUNCTION
          ? GROUP_HEADER_H
          : NODE_H,
    });
    const { positions, edges, merges, stages } = layout;
    const nodeMap = new Map<string, ChainTreeNode>();
    const parentMap = new Map<string, ChainTreeNode>();
    const collect = (n: ChainTreeNode, parent?: ChainTreeNode) => {
      nodeMap.set(n.ticker.id, n);
      if (parent) parentMap.set(n.ticker.id, parent);
      n.children.forEach(child => collect(child, n));
    };
    collect(tree);
    const maxX = layout.width + 60;
    const maxY = layout.height + 100;
    return { positions, edges, merges, stages, nodeMap, parentMap, maxX, maxY };
  }, [list, rootId]);

  const fitCanvas = useCallback(() => {
    const element = containerRef.current;
    if (!element || positions.length === 0) return;
    const rect = element.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return;
    const contentWidth = Math.max(1, maxX - 60);
    const contentHeight = Math.max(1, maxY - 100);
    const scaleX = (rect.width - 60) / contentWidth;
    const scaleY = (rect.height - 60) / contentHeight;
    const scale = Math.max(MIN_ZOOM, Math.min(scaleX, scaleY, 1));
    setZoom(scale);
    setPan({
      x: (rect.width - contentWidth * scale) / 2,
      y: Math.max(24, (rect.height - contentHeight * scale) / 2),
    });
  }, [maxX, maxY, positions.length]);

  // Open on the complete workflow instead of cropping tall branches behind
  // the logs panel. Users can zoom into individual cards afterward.
  useEffect(() => {
    fitCanvas();
  }, [fitCanvas]);

  const handleWheel = useCallback((e: React.WheelEvent) => {
    setZoom((z) => Math.max(MIN_ZOOM, Math.min(2, z * (e.deltaY > 0 ? 0.92 : 1.08))));
  }, []);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      if (e.button === 0 && (e.target as HTMLElement).closest("[data-canvas]")) {
        setIsPanning(true);
        setPanStart({ x: e.clientX - pan.x, y: e.clientY - pan.y });
      }
    },
    [pan]
  );

  const handleMouseMove = useCallback(
    (e: React.MouseEvent) => {
      if (isPanning) setPan({ x: e.clientX - panStart.x, y: e.clientY - panStart.y });
    },
    [isPanning, panStart]
  );

  const handleMouseUp = useCallback(() => setIsPanning(false), []);

  const totalCount = nodeMap.size;
  const groupCount = [...nodeMap.values()].filter(
    node => node.functionName === PARALLEL_GROUP_FUNCTION
  ).length;
  const jobCount = totalCount - groupCount;
  const selectedNode = selectedId ? nodeMap.get(selectedId) ?? null : null;
  const selectedParent = selectedNode ? parentMap.get(selectedNode.ticker.id) : undefined;
  const selectedParentResultId = selectedNode?.ticker.parentId
    ? selectedParent?.ticker.functionName === PARALLEL_GROUP_FUNCTION
      ? selectedParent.ticker.parentId
      : selectedNode.ticker.parentId
    : null;
  const selectedIsGroup =
    selectedNode?.ticker.functionName === PARALLEL_GROUP_FUNCTION;
  const selectedIsGroupedJob =
    selectedParent?.ticker.functionName === PARALLEL_GROUP_FUNCTION &&
    isGroupedRunCondition(selectedNode?.ticker.runCondition);
  const selectedProgressSucceeded = selectedNode
    ? isSuccessfulStatus(selectedNode.ticker.status)
    : false;
  const selectedLockHolder = resolveChainLockHolder(selectedId ?? "", list);
  const rootNode = nodeMap.get(rootId);
  const retryImpact = useMemo(
    () => rootNode && selectedId
      ? computeRetryImpact(rootNode, selectedId)
      : null,
    [rootNode, selectedId]
  );
  const deletionRestricted = !!rootNode && isChainDeletionRestricted(rootNode.ticker, list);
  const canEditChain =
    !isReadOnly && !!rootNode && !rootNode.ticker.originCronTickerId && (rootNode.ticker.status === "Idle" || rootNode.ticker.status === "Queued");

  return (
    <div className="flex flex-col h-full">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-b border-border bg-surface-0/50 shrink-0">
        <div className="flex flex-wrap items-center gap-3">
          <button
            onClick={onBack}
            className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors"
          >
            <ArrowLeft className="h-3 w-3" /> {backLabel}
          </button>
          <div className="h-4 w-px bg-border" />
          <GitBranch className="h-3.5 w-3.5 text-primary" />
          <span className="font-mono text-sm font-semibold">Chain Flowchart</span>
          {isPreview && <span className="text-[11px] text-muted-foreground">Chain template</span>}
          {ephemeralTest && (
            <span className="inline-flex items-center gap-1.5 rounded-full border border-primary/25 bg-primary/[0.07] px-2 py-0.5 text-[10px] font-medium text-primary">
              {ephemeralTestCompleted
                ? <Check className="h-3 w-3" />
                : <Loader2 className="h-3 w-3 animate-spin" />}
              {ephemeralTestCompleted ? "Test complete" : "Test running"} · View only
            </span>
          )}
          {chainQuery.data && (
            <span className="break-all text-[11px] text-muted-foreground/70 font-mono">{rootId}</span>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3 text-[11px] text-muted-foreground">
          <div
            className="hidden 2xl:flex items-center gap-3 rounded-md border border-border/60 bg-surface-1/60 px-2.5 py-1"
            title="Job cards show execution status. Colored paths show start rules. Cyan containers show durable groups."
          >
            <span className="inline-flex items-center gap-1.5">
              <span className="h-2.5 w-3.5 rounded-sm border border-status-healthy/70 bg-status-healthy/10" />
              Job card = status
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="w-4 border-t-2 border-status-healthy/70" />
              Path = start rule
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="w-4 border-t-2 border-workflow-sync/80" />
              Cyan = group
            </span>
          </div>
          {!isReadOnly && rootNode && <ChainDeleteBehaviorControl key={rootId} ticker={rootNode.ticker} />}
          {totalCount > 0 && (
            <span className="tabular-nums">
              {jobCount} {jobCount === 1 ? "job" : "jobs"}
              {groupCount > 0 && ` · ${groupCount} ${groupCount === 1 ? "group" : "groups"}`}
            </span>
          )}
          {canEditChain && onEditChain && (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs border-border text-muted-foreground"
              onClick={() => onEditChain(rootId)}
              disabled={deletionRestricted}
              title={deletionRestricted ? "Restrict waits until every descendant reaches a final status before replacing this chain." : undefined}
            >
              <Pencil className="h-3 w-3 mr-1.5" /> Edit chain
            </Button>
          )}
          <div className="flex items-center gap-1">
            <button
              onClick={fitCanvas}
              className="p-1 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
              title="Fit the complete chain to the canvas"
              aria-label="Fit chain to view"
            >
              <Maximize2 className="h-3 w-3" />
            </button>
            <button
              onClick={() => setZoom((z) => Math.min(2, z + 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              +
            </button>
            <span className="tabular-nums w-10 text-center">{Math.round(zoom * 100)}%</span>
            <button
              onClick={() => setZoom((z) => Math.max(MIN_ZOOM, z - 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              −
            </button>
          </div>
        </div>
      </div>

      {/* Canvas */}
      <div
        ref={containerRef}
        data-canvas
        className="flex-1 overflow-hidden bg-surface-0 relative select-none"
        style={{ cursor: isPanning ? "grabbing" : "grab" }}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onClick={(e) => {
          const t = e.target as HTMLElement;
          if (t.hasAttribute("data-canvas") || t.closest("[data-canvas-bg]")) {
            setSelectedId(null);
          }
        }}
      >
        {!hasExternalTickers && chainQuery.isLoading && (
          <div className="absolute inset-0 flex items-center justify-center text-xs text-muted-foreground gap-2">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading chain…
          </div>
        )}
        {!hasExternalTickers && chainQuery.isError && (
          <div className="absolute inset-0 flex items-center justify-center text-xs text-status-error">
            Failed to load chain.
          </div>
        )}

        {(hasExternalTickers || chainQuery.data) && (
          <div
            className="absolute"
            style={{
              transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
              transformOrigin: "0 0",
            }}
          >
            {/* SVG edges (with arrowheads) */}
            <svg
              width={maxX}
              height={maxY}
              className="absolute top-0 left-0 pointer-events-none"
              style={{ overflow: "visible" }}
            >
              {/* Group frames sit behind their job cards and connectors. The persisted
                  coordinator is represented by the integrated group header below. */}
              {[...stages]
                .sort((left, right) =>
                  right.bounds.width * right.bounds.height -
                  left.bounds.width * left.bounds.height)
                .map(stage => {
                const branchRoots = stage.branchRootIds
                  .map(id => positions.find(position => position.id === id))
                  .filter((position): position is NonNullable<typeof position> => !!position)
                  .sort((left, right) => left.x - right.x);
                const groupPosition = positions.find(position => position.id === stage.groupId);
                const groupBottom = groupPosition ? groupPosition.y + GROUP_HEADER_H : 0;
                const branchTop = branchRoots.length > 0
                  ? Math.min(...branchRoots.map(root => root.y))
                  : 0;
                const forkY = (groupBottom + branchTop) / 2;
                const branchCenters = branchRoots.map(root => root.x + NODE_W / 2);
                const forkColor = JOIN_PATH_COLOR;
                return (
                  <g key={`stage-frame-${stage.groupId}`}>
                    <rect
                      x={stage.bounds.x}
                      y={stage.bounds.y}
                      width={stage.bounds.width}
                      height={stage.bounds.height}
                      rx={16}
                      fill={selectedId === stage.groupId
                        ? "hsl(var(--workflow-sync) / 0.07)"
                        : "hsl(var(--workflow-sync) / 0.035)"}
                      stroke={selectedId === stage.groupId
                        ? "hsl(var(--workflow-sync) / 0.72)"
                        : "hsl(var(--workflow-sync) / 0.28)"}
                      strokeWidth={selectedId === stage.groupId ? 2.25 : 1.5}
                    />
                    <line
                      x1={stage.bounds.x}
                      x2={stage.bounds.x + stage.bounds.width}
                      y1={groupBottom}
                      y2={groupBottom}
                      stroke="hsl(var(--workflow-sync) / 0.22)"
                    />
                    {groupPosition && branchCenters.length > 0 && (
                      <g opacity={0.68}>
                        <path
                          d={`M ${groupPosition.x + NODE_W / 2} ${groupBottom} L ${groupPosition.x + NODE_W / 2} ${forkY} M ${Math.min(...branchCenters)} ${forkY} L ${Math.max(...branchCenters)} ${forkY}`}
                          fill="none"
                          stroke={forkColor}
                          strokeWidth={2}
                        />
                        {branchRoots.map(root => {
                          const targetX = root.x + NODE_W / 2;
                          return (
                            <g key={`parallel-fork-${stage.groupId}-${root.id}`}>
                              <line
                                x1={targetX}
                                x2={targetX}
                                y1={forkY}
                                y2={root.y}
                                stroke={forkColor}
                                strokeWidth={2}
                              />
                              <polygon
                                points={`${targetX - 3},${root.y - 5} ${targetX + 3},${root.y - 5} ${targetX},${root.y}`}
                                fill={forkColor}
                              />
                            </g>
                          );
                        })}
                      </g>
                    )}
                    {stage.outcomeBounds && (
                      <g>
                        <rect
                          x={stage.outcomeBounds.x}
                          y={stage.outcomeBounds.y}
                          width={stage.outcomeBounds.width}
                          height={stage.outcomeBounds.height}
                          rx={14}
                          fill="hsl(var(--surface-1) / 0.42)"
                          stroke="hsl(var(--border) / 0.72)"
                        />
                        <text
                          x={stage.outcomeBounds.x + 14}
                          y={stage.outcomeBounds.y + 16}
                          fontSize={8}
                          fontWeight={650}
                          letterSpacing={0.9}
                          fill="hsl(var(--muted-foreground) / 0.8)"
                        >
                          GROUP OUTCOMES
                        </text>
                      </g>
                    )}
                  </g>
                );
              })}
              {edges.map((edge) => {
                  const parent = positions.find((position) => position.id === edge.fromId);
                  const child = positions.find((position) => position.id === edge.toId);
                  if (!parent) return null;
                  if (!child) return null;
                  const childNode = nodeMap.get(edge.toId);
                  const parentNode = nodeMap.get(edge.fromId);
                  if (parentNode?.functionName === PARALLEL_GROUP_FUNCTION) return null;
                  const childStatus = childNode?.ticker.status;
                  const condition = childNode?.ticker.runCondition;
                  // Colored paths and pills describe the target's start rule.
                  // Runtime state remains on the card and affects only line style.
                  const color = condition
                    ? runConditionHsl(condition)
                    : CONNECTOR_COLOR;
                  const isPending =
                    childStatus === "Idle" ||
                    childStatus === "Queued";
                  const skipped = childStatus === "Skipped";
                  const parentHeight = parentNode?.functionName === PARALLEL_GROUP_FUNCTION
                    ? GROUP_HEADER_H
                    : NODE_H;
                  const x1 = parent.x + NODE_W / 2;
                  const y1 = parent.y + parentHeight;
                  const childStage = childNode?.functionName === PARALLEL_GROUP_FUNCTION
                    ? stages.find(stage => stage.groupId === childNode.id)
                    : undefined;
                  const x2 = childStage
                    ? childStage.bounds.x + childStage.bounds.width / 2
                    : child.x + NODE_W / 2;
                  const y2 = childStage ? childStage.bounds.y : child.y;
                  const midY = (y1 + y2) / 2;
                  return (
                    <g
                      key={`edge-${edge.fromId}-${edge.toId}`}
                      opacity={skipped ? 0.28 : isPending ? 0.45 : 0.7}
                    >
                      <path
                        d={`M ${x1} ${y1} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${y2}`}
                        fill="none"
                        stroke={color}
                        strokeWidth={2}
                        strokeDasharray={skipped ? "2 4" : isPending ? "6 4" : undefined}
                      />
                      <polygon
                        points={`${x2 - 3},${y2 - 5} ${x2 + 3},${y2 - 5} ${x2},${y2}`}
                        fill={color}
                      />
                    </g>
                  );
                })}
              {merges.map((merge, mergeIndex) => {
                const stage = stages.find(item => item.groupId === merge.groupId);
                if (!stage || merge.sourceIds.length === 0) return null;
                const branchStatusCollections = stage.branchRootIds.map(id => {
                  const branch = nodeMap.get(id);
                  return branch ? collectBranchStatuses(branch) : [];
                });
                const branchStatuses = branchStatusCollections.map(
                  aggregateBranchSubtreeStatus
                );
                const targets = merge.targetIds
                  .map(id => {
                    const position = positions.find(item => item.id === id);
                    const node = nodeMap.get(id);
                    return position && node ? { id, position, node } : null;
                  })
                  .filter((target): target is NonNullable<typeof target> => !!target);
                const summaryItems = layoutJoinSummary(
                  summarizeBranchStatuses(branchStatuses)
                );
                const summaryWidth = summaryItems.reduce(
                  (sum, item) => sum + item.width,
                  Math.max(0, summaryItems.length - 1) * 9
                );
                const settledCount = branchStatusCollections.filter(
                  isBranchSubtreeSettled
                ).length;
                const joinLabel = settledCount === branchStatuses.length
                  ? "ALL GROUP JOBS SETTLED"
                  : settledCount === 0
                    ? `WAITING FOR ${branchStatuses.length} JOBS`
                    : `${settledCount} OF ${branchStatuses.length} JOBS SETTLED`;
                const footerHeight = 30;
                const stageBottom = stage.bounds.y + stage.bounds.height;
                const footerY = stageBottom - footerHeight;
                const summaryAnchorX = stage.bounds.x + stage.bounds.width - summaryWidth / 2 - 16;
                const outcomeBranchY = targets.length > 0
                  ? Math.min(...targets.map(target => target.position.y)) - 24
                  : null;
                const targetCenters = targets.map(target => target.position.x + NODE_W / 2);
                return (
                  <g key={`merge-${mergeIndex}`}>
                    <rect
                      x={stage.bounds.x + 1}
                      y={footerY}
                      width={stage.bounds.width - 2}
                      height={footerHeight - 1}
                      fill="hsl(var(--workflow-sync) / 0.07)"
                    />
                    <line
                      x1={stage.bounds.x + 1}
                      x2={stage.bounds.x + stage.bounds.width - 1}
                      y1={footerY}
                      y2={footerY}
                      stroke={JOIN_PATH_COLOR}
                      strokeWidth={1.5}
                    />
                    <text
                      x={stage.bounds.x + 16}
                      y={footerY + 19}
                      fontSize={8}
                      fontWeight={650}
                      letterSpacing={0.7}
                      fill={JOIN_PATH_COLOR}
                    >
                      {joinLabel}
                    </text>
                    {summaryItems.map(item => (
                      <g
                        key={`merge-summary-${item.key}`}
                        transform={`translate(${summaryAnchorX + item.x} ${footerY + 16})`}
                      >
                        <circle cx={3} cy={0} r={2.5} fill={item.color} />
                        <text
                          x={9}
                          y={2.5}
                          fontSize={8}
                          fontWeight={500}
                          fill="hsl(var(--muted-foreground))"
                        >
                          {item.text}
                        </text>
                      </g>
                    ))}
                    {outcomeBranchY !== null && (
                      <>
                        <path
                          d={`M ${merge.anchorX} ${stageBottom} L ${merge.anchorX} ${outcomeBranchY}`}
                          fill="none"
                          stroke={JOIN_PATH_COLOR}
                          strokeWidth={2}
                        />
                        <line
                          x1={Math.min(...targetCenters)}
                          x2={Math.max(...targetCenters)}
                          y1={outcomeBranchY}
                          y2={outcomeBranchY}
                          stroke={JOIN_PATH_COLOR}
                          strokeWidth={2}
                        />
                      </>
                    )}
                    {outcomeBranchY !== null && targets.map(({ id, position, node }) => {
                      const color = node.ticker.runCondition
                        ? runConditionHsl(node.ticker.runCondition)
                        : CONNECTOR_COLOR;
                      const isPending =
                        node.ticker.status === "Idle" ||
                        node.ticker.status === "Queued";
                      const skipped = node.ticker.status === "Skipped";
                      const targetX = position.x + NODE_W / 2;
                      const targetY = position.y;
                      return (
                        <g
                          key={`outcome-edge-${merge.groupId}-${id}`}
                          opacity={skipped ? 0.28 : isPending ? 0.45 : 0.7}
                        >
                          <path
                            d={`M ${targetX} ${outcomeBranchY} L ${targetX} ${targetY}`}
                            fill="none"
                            stroke={color}
                            strokeWidth={2}
                            strokeDasharray={skipped ? "2 4" : isPending ? "6 4" : undefined}
                          />
                          <polygon
                            points={`${targetX - 3},${targetY - 5} ${targetX + 3},${targetY - 5} ${targetX},${targetY}`}
                            fill={color}
                          />
                          <circle
                            cx={targetX}
                            cy={outcomeBranchY}
                            r={2.5}
                            fill={JOIN_PATH_COLOR}
                          />
                        </g>
                      );
                    })}
                  </g>
                );
              })}
            </svg>

            {/* The coordinator is infrastructure, so it appears as the group's own
                header instead of pretending to be another executable job card. */}
            {stages.map(stage => {
              const group = nodeMap.get(stage.groupId);
              if (!group) return null;
              const groupPosition = positions.find(
                position => position.id === stage.groupId
              );
              if (!groupPosition) return null;
              const groupedJobCount = stage.branchRootIds.length;
              return (
                <button
                  key={`group-header-${stage.groupId}`}
                  type="button"
                  onClick={() => setSelectedId(
                    stage.groupId === selectedId ? null : stage.groupId
                  )}
                  className={cn(
                    "absolute z-[1] flex items-center gap-3 rounded-t-[15px] border-b px-4 text-left transition-colors",
                    "border-workflow-sync/25 bg-surface-1/90 hover:bg-workflow-sync/[0.08]",
                    selectedId === stage.groupId && "bg-workflow-sync/[0.11]"
                  )}
                  style={{
                    left: stage.bounds.x + 1,
                    top: stage.bounds.y + 1,
                    width: stage.bounds.width - 2,
                    height:
                      groupPosition.y + GROUP_HEADER_H - stage.bounds.y - 1,
                  }}
                  title="Open durable group details"
                >
                  <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-workflow-sync/10 text-workflow-sync">
                    <GitMerge className="h-3.5 w-3.5" />
                  </span>
                  <span className="min-w-0">
                    <span className="flex items-center gap-2">
                      <span className="text-[9px] font-semibold uppercase tracking-[0.14em] text-workflow-sync">
                        Group
                      </span>
                      <span className="text-[10px] text-muted-foreground">
                        {groupedJobCount} grouped {groupedJobCount === 1 ? "job" : "jobs"}
                      </span>
                    </span>
                    <span className="block truncate text-[10px] text-muted-foreground/70">
                      Runs matching jobs together and waits for their paths to settle
                    </span>
                  </span>
                  <span className="ml-auto flex shrink-0 items-center gap-2">
                    <StatusBadge
                      status={group.ticker.status}
                      label={groupStatusLabel(group.ticker.status)}
                      className="shrink-0 text-[9px]"
                    />
                  </span>
                </button>
              );
            })}

            {/* Nodes */}
            {positions.map((pos) => {
              const node = nodeMap.get(pos.id);
              if (!node) return null;
              const isRoot = pos.id === rootId;
              const t = node.ticker;
              const isParallelGroup = t.functionName === PARALLEL_GROUP_FUNCTION;
              const isIntegratedGroup = isParallelGroup &&
                stages.some(stage => stage.groupId === t.id);
              if (isIntegratedGroup) return null;
              const at = t.functionName?.indexOf("@") ?? -1;
              const lastDot = at < 0 ? t.functionName?.lastIndexOf(".") ?? -1 : -1;
              const bare = isParallelGroup
                ? "Group"
                : at > 0
                  ? t.functionName.slice(0, at)
                  : lastDot > 0
                    ? t.functionName.slice(lastDot + 1)
                  : t.functionName;
              const nodeName = isParallelGroup
                ? `${node.children.filter(child => isGroupedRunCondition(child.ticker.runCondition)).length} grouped ${node.children.filter(child => isGroupedRunCondition(child.ticker.runCondition)).length === 1 ? "job" : "jobs"}`
                : at > 0
                  ? t.functionName.slice(at + 1)
                  : lastDot > 0
                    ? t.functionName.slice(0, lastDot)
                    : "Ticker function";
              const conditionText = t.runCondition
                ? baseRunCondition(t.runCondition)
                : null;
              const skipped = !isRoot && t.status === "Skipped";
              const conditionState = conditionEvaluationState(
                t.status,
                Boolean(t.executedAt || t.lockedAt)
              );
              const statusVar = STATUS_CONFIG[t.status].cssVar;
              const statusColor = `hsl(${statusVar.replace(/^var\(([^)]+)\)$/, "var($1) / 0.4")})`;
              const quiet = t.status === "Idle" || t.status === "Queued" || skipped;
              const progressActive = t.status === "InProgress";
              const progressSucceeded = isSuccessfulStatus(t.status);
              const displayedProgress = progressSucceeded
                ? 100
                : Math.max(0, Math.min(100, t.progressPercent ?? 0));
              const ts = t.executedAt ?? t.scheduledFor;
              const timeLabel = quiet || !ts ? "—" : relativeTime(ts);
              const timeTooltip = quiet
                ? `Status: ${t.status}`
                : ts
                  ? `${t.executedAt ? "Executed" : "Scheduled"}: ${formatAbsolute(ts, timezone)}`
                  : "No execution time recorded";
              const elapsedLabel =
                quiet || t.elapsedTime <= 0 ? "—" : formatDuration(t.elapsedTime);
              const retriesLabel =
                t.retries > 0 ? (quiet ? "—" : `${t.retryCount}/${t.retries}`) : "—";
              return (
                <div
                  key={pos.id}
                  className="absolute"
                  style={{ left: pos.x, top: pos.y, width: NODE_W, height: NODE_H }}
                >
                  <div
                    onClick={() => setSelectedId(pos.id === selectedId ? null : pos.id)}
                    className={cn(
                      "w-full h-full rounded-xl border bg-card p-3 cursor-pointer transition-all duration-200 flex flex-col",
                      skipped && "border-dashed opacity-55 saturate-75 hover:opacity-80 hover:saturate-100",
                      selectedId === pos.id && "ring-1 ring-primary/50"
                    )}
                    style={{ borderColor: statusColor }}
                  >
                    <div className="flex items-center gap-2 mb-1.5">
                      {isRoot && (
                        <GitBranch className="h-3 w-3 text-primary shrink-0" />
                      )}
                      {isParallelGroup && (
                        <GitMerge className="h-3 w-3 text-workflow-sync shrink-0" />
                      )}
                      <span className="font-mono text-[11px] font-semibold truncate text-foreground">
                        {bare || "(unset)"}
                      </span>
                      <StatusBadge
                        status={t.status}
                        className="ml-auto shrink-0 text-[9px]"
                      />
                    </div>
                    <div
                      className="text-[9px] text-muted-foreground/55 font-mono truncate mb-1"
                      title={t.functionName || undefined}
                    >
                      {nodeName}
                    </div>
                    {t.description && t.description.trim().length > 0 && (
                      <p
                        className="text-[10px] text-muted-foreground line-clamp-2 mb-1"
                        title={t.description}
                      >
                        {t.description}
                      </p>
                    )}
                    <div className="mt-auto">
                      {!isRoot && t.runCondition && !isParallelGroup && (
                        <div className="min-w-0">
                          <span className="block text-[8px] font-medium uppercase tracking-[0.12em] text-muted-foreground/45">
                            Start condition
                          </span>
                          <code
                            className="block truncate text-[10px] leading-4 text-muted-foreground/90"
                            title={conditionText ?? undefined}
                          >
                            {conditionText}
                          </code>
                          <div className="mt-1 flex items-center gap-2">
                            <ConditionEvaluationBadge state={conditionState} />
                            {!isPreview && (
                              <span className="ml-auto text-[9px] text-muted-foreground/50">
                                Priority · {t.priority}
                              </span>
                            )}
                          </div>
                        </div>
                      )}
                      {!isPreview && (isRoot || !t.runCondition || isParallelGroup) && (
                        <div className="mt-1 text-right text-[9px] text-muted-foreground/50">
                          Priority · {t.priority}
                        </div>
                      )}
                    </div>
                    {t.progressPercent != null && (
                      <div
                        className={cn(
                          "mt-1.5 transition-opacity",
                          !progressActive && !progressSucceeded && "opacity-45"
                        )}
                        title={progressSucceeded
                          ? "Completed successfully"
                          : t.progressMessage || `Progress ${t.progressPercent}%`}
                      >
                        <div className={cn(
                          "mb-1 flex items-center justify-between gap-2 text-[9px]",
                          progressSucceeded
                            ? "text-status-healthy"
                            : progressActive
                              ? "text-muted-foreground/70"
                              : "text-muted-foreground/55"
                        )}>
                          <span className="truncate">
                            {progressSucceeded ? "Completed" : t.progressMessage || "Progress"}
                          </span>
                          <span className="shrink-0 tabular-nums">{Math.round(displayedProgress)}%</span>
                        </div>
                        <div className="h-1 overflow-hidden rounded-full bg-surface-2">
                          <div
                            className={cn(
                              "h-full rounded-full transition-[width] duration-300",
                              progressSucceeded
                                ? "bg-status-healthy"
                                : progressActive
                                  ? "bg-primary"
                                  : "bg-muted-foreground/50"
                            )}
                            style={{ width: `${displayedProgress}%` }}
                          />
                        </div>
                      </div>
                    )}
                    <div
                      className={cn(
                        "mt-2 pt-2 border-t border-border/50 flex items-center gap-2 text-[10px] flex-wrap",
                        skipped ? "text-muted-foreground/30" : "text-muted-foreground/80"
                      )}
                    >
                      <span className="flex items-center gap-1 min-w-0" title={timeTooltip}>
                        <Clock className="h-2.5 w-2.5 shrink-0" />
                        <span className="truncate">{timeLabel}</span>
                      </span>
                      <span
                        className="flex items-center gap-1 tabular-nums"
                        title={quiet ? `Status: ${t.status}` : "Elapsed time of the last run"}
                      >
                        <Timer className="h-2.5 w-2.5 shrink-0" />
                        {elapsedLabel}
                      </span>
                      <span
                        className={cn(
                          "flex items-center gap-1 tabular-nums ml-auto",
                          !quiet && t.retryCount > 0 ? "text-status-warning" : ""
                        )}
                        title={
                          t.retries > 0
                            ? quiet
                              ? `Status: ${t.status}`
                              : `${t.retryCount}/${t.retries} retries used`
                            : "No retries configured"
                        }
                      >
                        <RotateCcw className="h-2.5 w-2.5 shrink-0" />
                        {retriesLabel}
                      </span>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* Dot grid background (matches Hub) */}
        <div
          data-canvas-bg
          className="absolute inset-0 pointer-events-none opacity-[0.03]"
          style={{
            backgroundImage:
              "radial-gradient(circle, currentColor 1px, transparent 1px)",
            backgroundSize: "24px 24px",
          }}
        />
      </div>

      {/* Resizable logs panel pinned to the bottom. Default: merged chain logs.
          Selecting a node scopes to that ticker's single-log tail. */}
      <div
        className="shrink-0 flex min-w-0 flex-col transition-[margin] duration-200"
        style={{
          height: logsPanelHeight,
          marginRight: selectedNode ? DETAIL_RAIL_WIDTH : 0,
        }}
      >
        <div
          onMouseDown={handleLogsResizeStart}
          className="h-1.5 cursor-row-resize bg-border/50 hover:bg-primary/30 transition-colors shrink-0"
          title="Drag to resize"
        />
        {isPreview ? (
          <div className="p-4 text-xs text-muted-foreground">Logs will appear when this occurrence launches.</div>
        ) : selectedNode ? (
          <>
            <div className="flex items-center justify-between gap-3 px-4 py-2 border-y border-border bg-surface-0/40 shrink-0">
              <div className="flex items-center gap-3 text-[11px] min-w-0">
                <span className="text-muted-foreground/60 shrink-0">Logs for</span>
                <span className="font-mono font-medium truncate">
                  {selectedNode.ticker.functionName === PARALLEL_GROUP_FUNCTION
                    ? "Group"
                    : selectedNode.ticker.functionName}
                </span>
                <StatusBadge
                  status={selectedNode.ticker.status}
                  label={selectedIsGroup
                    ? groupStatusLabel(selectedNode.ticker.status)
                    : undefined}
                  className="shrink-0 text-[9px]"
                />
              </div>
              <button
                onClick={() => setSelectedId(null)}
                className="shrink-0 text-[10px] font-medium text-primary/80 transition-colors hover:text-primary"
              >
                Show whole chain
              </button>
            </div>
            <div className="h-full min-h-0 w-full flex-1 overflow-hidden">
              <LogsPanel
                functionName={selectedNode.ticker.functionName}
                status={selectedNode.ticker.status}
                tickerId={selectedNode.ticker.id}
                exceptionMessage={selectedNode.ticker.exceptionMessage}
                skippedReason={selectedNode.ticker.skippedReason}
                executedAt={selectedNode.ticker.executedAt}
                fillHeight
                className="rounded-none border-x-0 border-b-0"
              />
            </div>
          </>
        ) : (
          <div className="h-full min-h-0 w-full flex-1 overflow-hidden">
              <ChainLogsPanel
              rootId={rootId}
                tickers={ephemeralTest ? list : undefined}
              fillHeight
              className="rounded-none border-x-0 border-b-0"
            />
          </div>
        )}
      </div>

      {/* Detail rail (right). It owns the full-height right column; the logs
          panel reserves the same width while the rail is open. */}
      {selectedNode && (
        <div
          className="absolute top-0 right-0 bg-surface-1 border-l border-border p-5 overflow-y-auto scrollbar-thin shadow-lg"
          style={{ bottom: 0, width: DETAIL_RAIL_WIDTH }}
        >
          <div className="flex items-center justify-between mb-4">
            <span className="text-sm font-semibold">
              {selectedIsGroup ? "Group details" : "Job details"}
            </span>
            <button
              onClick={() => setSelectedId(null)}
              className="text-muted-foreground hover:text-foreground text-xs"
            >
              ×
            </button>
          </div>
          <div className="space-y-3 text-[11px]">
            <div className="space-y-2">
              {!selectedIsGroup && !isReadOnly && (
                <CancelTickerButton id={selectedNode.ticker.id} status={selectedNode.ticker.status}
                  hasChildren={selectedNode.children.length > 0} onChanged={() => { void chainQuery.refetch(); }} />
              )}
              {!isReadOnly && (
                <Button
                  variant="outline"
                  size="sm"
                  className="h-8 w-full text-xs"
                  disabled={retryMutation.isPending || selectedNode.ticker.status === "InProgress"}
                  onClick={() => setRetryDialogOpen(true)}
                >
                  {retryMutation.isPending ? <Loader2 className="mr-1.5 h-3 w-3 animate-spin" /> : <RotateCcw className="mr-1.5 h-3 w-3" />}
                  {selectedIsGroup ? "Retry this group" : "Retry from this job"}
                </Button>
              )}
              {!isReadOnly && retryImpact && (
                <AlertDialog open={retryDialogOpen} onOpenChange={setRetryDialogOpen}>
                  <AlertDialogContent className="sm:max-w-md">
                    <AlertDialogHeader>
                      <AlertDialogMedia className="bg-primary/10 text-primary">
                        <RotateCcw />
                      </AlertDialogMedia>
                      <AlertDialogTitle>
                        {selectedIsGroup ? "Retry this group?" : "Retry from this job?"}
                      </AlertDialogTitle>
                      <AlertDialogDescription>
                        {selectedIsGroup
                          ? "TickerQ keeps the existing chain and job IDs, reruns every job in this group, then re-evaluates its outcome jobs."
                          : "TickerQ keeps the existing chain and job IDs, then re-evaluates the affected path from this point."}
                      </AlertDialogDescription>
                    </AlertDialogHeader>

                    <div className="space-y-2 rounded-lg border border-border/70 bg-surface-1/70 p-3 text-xs">
                      <div className="flex items-start justify-between gap-4">
                        <span className="text-muted-foreground">
                          {selectedIsGroup ? "Selected group" : "Selected job"}
                        </span>
                        <span className="max-w-[230px] truncate text-right font-mono font-medium" title={selectedIsGroup ? "Group" : retryImpact.selectedJobName}>
                          {selectedIsGroup ? "Group" : retryImpact.selectedJobName}
                        </span>
                      </div>
                      <div className="flex items-start justify-between gap-4">
                        <span className="text-muted-foreground">
                          {selectedIsGroup ? "Group execution" : "Downstream tree"}
                        </span>
                        <span className="text-right font-medium">
                          {retryImpact.downstreamJobCount === 0
                            ? "No child jobs affected"
                            : `${retryImpact.downstreamJobCount} ${retryImpact.downstreamJobCount === 1 ? "job" : "jobs"} reset`}
                        </span>
                      </div>
                      <div className="flex items-start justify-between gap-4">
                        <span className="text-muted-foreground">Group outcomes</span>
                        <span className="text-right font-medium">
                          {retryImpact.recalculatedGroupCount === 0
                            ? "Not affected"
                            : selectedIsGroup
                              ? "Re-evaluated after the group settles"
                              : `${retryImpact.additionalOutcomeJobCount} additional ${retryImpact.additionalOutcomeJobCount === 1 ? "job" : "jobs"} re-evaluated`}
                        </span>
                      </div>
                      {retryImpact.reusedGroupedBranchCount > 0 && (
                        <div className="flex items-start justify-between gap-4">
                          <span className="text-muted-foreground">Settled group branches</span>
                          <span className="text-right font-medium">
                            {retryImpact.reusedGroupedBranchCount} reused
                          </span>
                        </div>
                      )}
                    </div>

                    <div className="text-xs text-muted-foreground">
                      <span className="font-medium text-foreground/85">Runs again: </span>
                      {retryImpact.affectedJobNames.slice(0, 4).join(", ")}
                      {retryImpact.affectedJobNames.length > 4 &&
                        ` and ${retryImpact.affectedJobNames.length - 4} more`}
                    </div>

                    <AlertDialogFooter>
                      <AlertDialogCancel disabled={retryMutation.isPending}>Keep current result</AlertDialogCancel>
                      <AlertDialogAction
                        disabled={retryMutation.isPending}
                        onClick={() => {
                          retryMutation.mutate(selectedNode.ticker.id, {
                            onSuccess: result => {
                              toast.success(selectedIsGroup
                                ? "Group retry started"
                                : "Chain retry started from this job", {
                                description: retryImpact.affectedJobIds.length === 1
                                  ? "No other jobs were reset."
                                  : `${retryImpact.affectedJobIds.length} jobs will be re-evaluated.`,
                              });
                              navigate(`/time-tickers/${result.id}/flowchart`);
                            },
                            onError: error => toast.error(selectedIsGroup
                              ? "Could not retry this group"
                              : "Could not retry from this job", {
                              description: error instanceof Error ? error.message : undefined,
                            }),
                          });
                        }}
                      >
                        {retryMutation.isPending && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
                        {selectedIsGroup
                          ? "Retry group"
                          : retryImpact.affectedJobIds.length === 1
                            ? "Retry job"
                            : "Retry affected path"}
                      </AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
              )}
            </div>
            <Detail label={selectedIsGroup ? "Type" : "Function"}>
              <span className="font-mono">
                {selectedIsGroup
                  ? "Durable group coordinator"
                  : selectedNode.ticker.functionName || "—"}
              </span>
            </Detail>
            <Detail label={selectedIsGroup ? "Group status" : "Status"}>
              <StatusBadge
                status={selectedNode.ticker.status}
                label={selectedIsGroup
                  ? groupStatusLabel(selectedNode.ticker.status)
                  : undefined}
              />
            </Detail>
            {selectedIsGroup && (
              <Detail label="Status meaning">
                {describeGroupStatus(selectedNode.ticker.status)}
              </Detail>
            )}
            <Detail label="Lock holder">
              <LockHolderValue value={selectedLockHolder.value} />
            </Detail>
            {selectedIsGroupedJob && (
              <Detail label="Group role">Grouped job</Detail>
            )}
            {selectedIsGroup && (
              <Detail label="Group behavior">
                Starts matching grouped jobs together, waits for their descendants, then evaluates final jobs
              </Detail>
            )}
            {selectedNode.ticker.runCondition && !selectedIsGroup && (
              <Detail label="Start condition">
                <code className="text-foreground/85">
                  {baseRunCondition(selectedNode.ticker.runCondition)}
                </code>
              </Detail>
            )}
            {selectedNode.ticker.parentId && (
              <Detail label="Parent job">
                <div className="flex items-center gap-1 rounded-md border border-border/60 bg-surface-0/40 p-1 transition-colors hover:border-primary/35 hover:bg-primary/[0.04]">
                  <button
                    type="button"
                    onClick={() => setSelectedId(selectedNode.ticker.parentId)}
                    className="group/parent flex min-w-0 flex-1 items-start gap-2 rounded px-1.5 py-1 text-left"
                    title="Show parent job details"
                  >
                    <CornerUpLeft className="mt-0.5 h-3.5 w-3.5 shrink-0 text-primary/75" />
                    <span className="min-w-0">
                      <span className="block truncate font-medium text-foreground/85 group-hover/parent:text-primary">
                        {selectedParent?.ticker.functionName === PARALLEL_GROUP_FUNCTION
                          ? "Group"
                          : selectedParent?.ticker.functionName || "Parent job"}
                      </span>
                      <span className="block break-all font-mono text-[9px] text-muted-foreground/65">
                        {selectedNode.ticker.parentId}
                      </span>
                    </span>
                  </button>
                  <CopyIdButton value={selectedNode.ticker.parentId} label="parent job ID" />
                </div>
              </Detail>
            )}
            {!selectedIsGroup && (
              <>
                {!isPreview && <Detail label="Priority">{selectedNode.ticker.priority}</Detail>}
                <Detail label="Retries">
                  {selectedNode.ticker.retryCount}/{selectedNode.ticker.retries}
                </Detail>
              </>
            )}
            {selectedNode.ticker.progressPercent != null && !selectedIsGroup && (
              <Detail label="Progress">
                <div className={cn(
                  "space-y-1 transition-opacity",
                  selectedNode.ticker.status !== "InProgress" &&
                    !selectedProgressSucceeded &&
                    "opacity-50"
                )}>
                  <div className="flex items-center justify-between gap-2">
                    <span className={cn(
                      "truncate",
                      selectedProgressSucceeded ? "text-status-healthy" : "text-muted-foreground"
                    )}>
                      {selectedProgressSucceeded
                        ? "Completed successfully"
                        : selectedNode.ticker.progressMessage || "Reported by the job"}
                    </span>
                    <span className="tabular-nums">
                      {selectedProgressSucceeded
                        ? 100
                        : Math.round(selectedNode.ticker.progressPercent)}%
                    </span>
                  </div>
                  <div className="h-1.5 overflow-hidden rounded-full bg-surface-2">
                    <div
                      className={cn(
                        "h-full rounded-full",
                        selectedProgressSucceeded
                          ? "bg-status-healthy"
                          : selectedNode.ticker.status === "InProgress"
                            ? "bg-primary"
                            : "bg-muted-foreground/50"
                      )}
                      style={{
                        width: `${selectedProgressSucceeded
                          ? 100
                          : Math.max(0, Math.min(100, selectedNode.ticker.progressPercent))}%`,
                      }}
                    />
                  </div>
                </div>
              </Detail>
            )}
            {selectedNode.ticker.scheduledFor && (
              <Detail label="Scheduled for">
                {formatAbsolute(selectedNode.ticker.scheduledFor, timezone)}
              </Detail>
            )}
            {selectedNode.ticker.executedAt && (
              <Detail label="Executed at">
                {formatAbsolute(selectedNode.ticker.executedAt, timezone)}
              </Detail>
            )}
            {!isPreview && <Detail label="ID">
              <div className="flex items-start gap-1">
                <span className="min-w-0 flex-1 break-all font-mono text-muted-foreground/70">
                  {selectedNode.ticker.id}
                </span>
                <CopyIdButton value={selectedNode.ticker.id} label="job ID" />
              </div>
            </Detail>}
            {!isReadOnly && !selectedIsGroup && (
              <JobPayloadDetails
                id={selectedNode.ticker.id}
                parentResultId={selectedParentResultId}
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function CopyIdButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1200);
    } catch {
      toast.error(`Could not copy ${label}`);
    }
  };

  return (
    <button
      type="button"
      onClick={() => { void copy(); }}
      className="flex h-6 w-6 shrink-0 items-center justify-center rounded text-muted-foreground/55 transition-colors hover:bg-surface-2 hover:text-foreground"
      title={copied ? "Copied" : `Copy ${label}`}
      aria-label={copied ? `${label} copied` : `Copy ${label}`}
    >
      {copied ? <Check className="h-3 w-3 text-status-healthy" /> : <Copy className="h-3 w-3" />}
    </button>
  );
}

function Detail({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[9px] uppercase tracking-wider text-muted-foreground/60">
        {label}
      </span>
      <div>{children}</div>
    </div>
  );
}
